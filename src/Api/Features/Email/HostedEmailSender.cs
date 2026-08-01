namespace Keepr.Api.Features.Email;

/// <summary>
/// Shared base for the hosted HTTP-API providers (Resend/Brevo/Mailgun). Each is a single POST that
/// differs only in URL, auth header, and body encoding — the base owns validation, the send, and
/// turning a non-2xx response into a throw, so a failed hosted send behaves like a failed SMTP send
/// and callers treat it identically. Constructed per send by <see cref="EmailSenderFactory"/> with a
/// short-timeout <see cref="HttpClient"/> and the decrypted settings. See
/// docs/feature-36-email-providers.md §2.1/§5.
/// </summary>
public abstract class HostedEmailSender(HttpClient http, ResolvedEmailSettings settings) : IEmailSender
{
    protected HttpClient Http { get; } = http;
    protected ResolvedEmailSettings Settings { get; } = settings;

    /// <summary>Human name for error messages, e.g. "Resend".</summary>
    protected abstract string ProviderName { get; }

    /// <summary>Builds the provider-specific request (URL, auth, body) for one message.</summary>
    protected abstract HttpRequestMessage BuildRequest(EmailMessage message);

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Settings.ApiKey))
            throw new InvalidOperationException(
                $"{ProviderName} is selected but no API key is configured.");

        using var request = BuildRequest(message);
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            // Include the provider's own message for diagnosis (surfaced in the test result / logs).
            // It's the provider's response about *our* request and does not contain the API key.
            throw new HttpRequestException(
                $"{ProviderName} rejected the message ({(int)response.StatusCode}): {Truncate(body, 500)}");
        }
    }

    /// <summary>An RFC 5322 From value: "Display Name &lt;addr&gt;", or bare address if no name.</summary>
    protected string From() =>
        string.IsNullOrWhiteSpace(Settings.FromName)
            ? Settings.FromAddress
            : $"{Settings.FromName} <{Settings.FromAddress}>";

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
