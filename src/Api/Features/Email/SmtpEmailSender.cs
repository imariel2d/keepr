using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Keepr.Api.Features.Email;

/// <summary>
/// Sends over SMTP via MailKit — the generic baseline that works with any provider exposing SMTP
/// credentials (Gmail, SES, SendGrid, Mailgun, Postmark, Resend, …). MailKit rather than the
/// built-in <c>System.Net.Mail.SmtpClient</c>, which Microsoft's own docs flag as not recommended
/// for new development. See docs/feature-36-account-provisioning.md §6.2.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _opt = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_opt.FromName, _opt.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        }.ToMessageBody();

        using var client = new SmtpClient();

        // Cap how long a dead host can hold the admin's request. MailKit's default is 120s; this
        // send runs inline on the request, so a firewalled host must fail fast. A linked token with
        // the same deadline makes the awaits actually cancel, not just the socket timeout.
        client.Timeout = _opt.Smtp.TimeoutSeconds * 1000;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_opt.Smtp.TimeoutSeconds));
        var token = cts.Token;

        // StartTls for submission ports (587); implicit TLS only on 465; anything else is an
        // unencrypted dev relay. Credentials must never cross an unencrypted link — the IsSecure
        // guard below enforces that regardless of how the socket ended up.
        var socketOptions = _opt.Smtp.UseStartTls ? SecureSocketOptions.StartTls
            : _opt.Smtp.Port == 465 ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.Auto;
        await client.ConnectAsync(_opt.Smtp.Host, _opt.Smtp.Port, socketOptions, token);

        // Some relays (or dev catchers like MailHog) accept unauthenticated mail; only authenticate
        // when a username is configured — and refuse to send those credentials in the clear.
        if (!string.IsNullOrEmpty(_opt.Smtp.Username))
        {
            if (!client.IsSecure)
                throw new InvalidOperationException(
                    "Refusing to send SMTP credentials over an unencrypted connection. Set "
                    + "Email__Smtp__UseStartTls=true (port 587) or use an implicit-TLS port (465).");
            await client.AuthenticateAsync(_opt.Smtp.Username, _opt.Smtp.Password, token);
        }

        await client.SendAsync(mime, token);
        await client.DisconnectAsync(true, token);
    }
}
