namespace Keepr.Api.Features.Email;

/// <summary>One outbound message. Carries both an <paramref name="HtmlBody"/> and a
/// <paramref name="TextBody"/>: a well-formed email ships a plain-text alternative for clients that
/// strip HTML.</summary>
public sealed record EmailMessage(
    string ToEmail, string ToName, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Sends outbound email. Deliberately narrow and provider-agnostic — modelled on
/// <see cref="Auth.IRegistrationGate"/>: swapping SMTP for an HTTP-API provider (Resend, SendGrid)
/// is a new implementation plus one line in Program.cs, and no caller changes.
///
/// <see cref="SendAsync"/> throws on transport failure; each caller decides whether that is fatal.
/// The invite path (§8.3) treats a failed send as non-fatal — the account was already committed.
/// See docs/feature-36-account-provisioning.md §6.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
