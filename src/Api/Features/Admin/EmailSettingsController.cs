using System.Net.Mail;
using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Email;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Keepr.Api.Features.Admin;

/// <summary>Email settings as the admin screen sees them. Deliberately carries <see cref="HasApiKey"/>,
/// never the key itself — the plaintext key is write-only (§6).</summary>
public record EmailSettingsResponse(
    string Provider, string FromAddress, string FromName, bool HasApiKey,
    string? MailgunDomain, string? MailgunRegion, string PublicBaseUrl, int InviteExpiryDays,
    DateTimeOffset? LastTestAt, bool? LastTestOk, string? LastTestError);

/// <param name="Provider">"none" | "resend" | "brevo" | "mailgun".</param>
/// <param name="FromAddress">Envelope/header From address; required for a hosted provider.</param>
/// <param name="FromName">Display name on the From address.</param>
/// <param name="ApiKey">Write-only. Blank/omitted keeps the stored key <b>only if the provider is
/// unchanged</b>; a provider change requires a new key; ignored when provider is "none".</param>
/// <param name="MailgunDomain">Mailgun only: the sending domain.</param>
/// <param name="MailgunRegion">Mailgun only: "us" or "eu".</param>
/// <param name="PublicBaseUrl">Absolute http(s) origin for links in emails; blank falls back to the
/// share viewer's origin.</param>
/// <param name="InviteExpiryDays">How long an emailed invite stays claimable; at least 1.</param>
public record UpdateEmailSettingsRequest(
    string Provider, string? FromAddress, string? FromName, string? ApiKey,
    string? MailgunDomain, string? MailgunRegion, string? PublicBaseUrl, int? InviteExpiryDays);

/// <summary>The result of a test send: whether it went out, and the provider error if it didn't.</summary>
public record EmailTestResult(bool Ok, string? Error);

/// <summary>
/// Admin-only management of the app-wide outbound-email provider (#36). Turns email config from a
/// boot-time env setting into runtime settings an admin edits here. Restricted by the "Admin" policy
/// (401 anonymous, 403 non-admin). See docs/feature-36-email-providers.md §6.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/email-settings")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class EmailSettingsController(
    AppDbContext db,
    EmailSettingsService settings,
    EmailSenderFactory senders,
    AdminAuditService audit,
    TimeProvider clock) : ControllerBase
{
    // Best-effort, process-local guard so a double-click can't fan out two test emails. Not
    // cross-instance by design (§6) — test sends are a rare, low-harm admin action.
    private static int _testInFlight;

    /// <summary>The current email settings for the admin screen (never the key).</summary>
    [HttpGet]
    [ProducesResponseType<EmailSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailSettingsResponse>> Get(CancellationToken ct) =>
        ToResponse(await settings.GetRowAsync(ct));

    /// <summary>
    /// Saves the email settings. The API key is write-only: blank keeps the stored key only when the
    /// provider is unchanged, a provider change requires a new key, and "none" clears it. Writes a
    /// secret-free <c>EmailSettingsChanged</c> audit entry.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<EmailSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EmailSettingsResponse>> Update(
        UpdateEmailSettingsRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (!Enum.TryParse<EmailProvider>(req.Provider, ignoreCase: true, out var provider)
            || !Enum.IsDefined(provider))
            return BadRequest(FieldErrors(("provider", "Provider must be one of none, resend, brevo, mailgun.")));

        var row = await settings.GetRowAsync(ct);
        var providerChanged = provider != row.Provider;

        // --- API key: write-only, provider-aware -------------------------------------------------
        var keyChanged = false;
        var newCipher = row.ApiKeyCipher;
        if (provider == EmailProvider.None)
        {
            // Nothing to authenticate — drop any stored key so the NULL-when-none invariant holds.
            if (row.ApiKeyCipher is not null) { newCipher = null; keyChanged = true; }
        }
        else
        {
            var providedKey = req.ApiKey?.Trim();
            if (!string.IsNullOrEmpty(providedKey))
            {
                newCipher = settings.Encrypt(providedKey);
                keyChanged = true;
            }
            else if (providerChanged)
                AddError(errors, "apiKey", "A new API key is required when changing provider.");
            else if (row.ApiKeyCipher is null)
                AddError(errors, "apiKey", "An API key is required.");
            // else: unchanged provider, blank key → keep the stored key.
        }

        // --- Provider-specific required fields ---------------------------------------------------
        var fromAddress = req.FromAddress?.Trim() ?? "";
        string? mailgunRegion = null;
        if (provider != EmailProvider.None)
        {
            if (string.IsNullOrWhiteSpace(fromAddress) || !MailAddress.TryCreate(fromAddress, out _))
                AddError(errors, "fromAddress", "A valid From address is required.");

            if (provider == EmailProvider.Mailgun)
            {
                if (string.IsNullOrWhiteSpace(req.MailgunDomain))
                    AddError(errors, "mailgunDomain", "A Mailgun sending domain is required.");
                mailgunRegion = req.MailgunRegion?.Trim().ToLowerInvariant();
                if (mailgunRegion is not ("us" or "eu"))
                    AddError(errors, "mailgunRegion", "Mailgun region must be 'us' or 'eu'.");
            }
        }

        // --- Cross-cutting fields ----------------------------------------------------------------
        var publicBaseUrl = req.PublicBaseUrl?.Trim() ?? row.PublicBaseUrl;
        if (!string.IsNullOrWhiteSpace(publicBaseUrl)
            && !(Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var u)
                 && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)))
            AddError(errors, "publicBaseUrl", "Public base URL must be an absolute http(s) URL.");

        var inviteExpiryDays = req.InviteExpiryDays ?? row.InviteExpiryDays;
        if (inviteExpiryDays < 1)
            AddError(errors, "inviteExpiryDays", "Invite expiry must be at least 1 day.");

        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join(" ", errors.Values.SelectMany(v => v))
            });

        // --- Apply -------------------------------------------------------------------------------
        row.Provider = provider;
        row.ApiKeyCipher = newCipher;
        row.FromAddress = fromAddress;
        row.FromName = req.FromName?.Trim() ?? row.FromName;
        // Mailgun's fields are only meaningful for Mailgun; clear them otherwise so nothing stale rides along.
        row.MailgunDomain = provider == EmailProvider.Mailgun ? req.MailgunDomain?.Trim() : null;
        row.MailgunRegion = provider == EmailProvider.Mailgun ? mailgunRegion : null;
        row.PublicBaseUrl = publicBaseUrl;
        row.InviteExpiryDays = inviteExpiryDays;
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedByUserId = User.UserId();

        audit.RecordEmailSettingsChanged(User.UserId(), ActorEmail(), row, keyChanged);
        await db.SaveChangesAsync(ct);

        return ToResponse(row);
    }

    /// <summary>
    /// Sends a test email to the acting admin's own address using the <b>saved</b> settings, and
    /// records the result. There is no request body — save first, then test (§6/§7).
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType<EmailTestResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailTestResult>> SendTest(CancellationToken ct)
    {
        if (!await settings.IsEnabledAsync(ct))
            return Problem("Email delivery is not configured.", statusCode: StatusCodes.Status409Conflict);

        if (Interlocked.CompareExchange(ref _testInFlight, 1, 0) == 1)
            return Problem("A test send is already in progress.", statusCode: StatusCodes.Status409Conflict);

        try
        {
            var toEmail = ActorEmail();
            var sender = await senders.CreateAsync(ct);
            var row = await settings.GetRowAsync(ct);
            try
            {
                await sender.SendAsync(new EmailMessage(
                    toEmail, string.Empty, "Keepr email test",
                    "<p>This is a test email from Keepr. Your email provider is configured correctly.</p>",
                    "This is a test email from Keepr. Your email provider is configured correctly."), ct);
                row.LastTestOk = true;
                row.LastTestError = null;
            }
            catch (Exception ex)
            {
                row.LastTestOk = false;
                row.LastTestError = Truncate(ex.Message, 2000);
            }
            row.LastTestAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new EmailTestResult(row.LastTestOk == true, row.LastTestError);
        }
        finally
        {
            Interlocked.Exchange(ref _testInFlight, 0);
        }
    }

    private static EmailSettingsResponse ToResponse(EmailSettings r) => new(
        r.Provider.ToString().ToLowerInvariant(), r.FromAddress, r.FromName,
        r.ApiKeyCipher is { Length: > 0 }, r.MailgunDomain, r.MailgunRegion,
        r.PublicBaseUrl, r.InviteExpiryDays, r.LastTestAt, r.LastTestOk, r.LastTestError);

    private static void AddError(Dictionary<string, string[]> errors, string field, string message) =>
        errors[field] = [message];

    private static ValidationProblemDetails FieldErrors(params (string Field, string Message)[] fields)
    {
        var dict = fields.ToDictionary(f => f.Field, f => new[] { f.Message });
        return new ValidationProblemDetails(dict)
        {
            Status = StatusCodes.Status400BadRequest,
            Detail = string.Join(" ", fields.Select(f => f.Message))
        };
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private string ActorEmail() => User.FindFirst(KeeprClaims.Email)?.Value ?? string.Empty;
}
