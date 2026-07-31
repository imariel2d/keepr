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

        // StartTls for the common submission port (587); Auto lets MailKit pick for implicit-TLS
        // setups (465). Either way the connection is encrypted before credentials are sent.
        var socketOptions = _opt.Smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(_opt.Smtp.Host, _opt.Smtp.Port, socketOptions, ct);

        // Some relays (or dev catchers like MailHog) accept unauthenticated mail; only authenticate
        // when a username is configured.
        if (!string.IsNullOrEmpty(_opt.Smtp.Username))
            await client.AuthenticateAsync(_opt.Smtp.Username, _opt.Smtp.Password, ct);

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}
