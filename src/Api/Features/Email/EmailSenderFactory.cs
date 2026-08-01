using Keepr.Api.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Keepr.Api.Features.Email;

/// <summary>
/// Resolves the right <see cref="IEmailSender"/> for the current settings, per send — this is what
/// replaces the boot-time DI choice so a provider switch takes effect immediately (§5). Order: a DB
/// hosted provider (Resend/Brevo/Mailgun); else the env SMTP fallback if configured; else the no-op.
/// See docs/feature-36-email-providers.md §5.1.
/// </summary>
public class EmailSenderFactory(
    EmailSettingsService settings, IHttpClientFactory httpFactory, IServiceProvider services)
{
    /// <summary>Named client carrying the short inline timeout (registered in Program.cs).</summary>
    public const string HttpClientName = "email";

    public async Task<IEmailSender> CreateAsync(CancellationToken ct)
    {
        var resolved = await settings.GetAsync(ct);
        return resolved.Provider switch
        {
            EmailProvider.Resend => new ResendEmailSender(httpFactory.CreateClient(HttpClientName), resolved),
            EmailProvider.Brevo => new BrevoEmailSender(httpFactory.CreateClient(HttpClientName), resolved),
            EmailProvider.Mailgun => new MailgunEmailSender(httpFactory.CreateClient(HttpClientName), resolved),

            // None → the env SMTP fallback keeps existing deployments working; otherwise nothing goes
            // out and NoOp logs the drop. SmtpEmailSender still reads its config from EmailOptions/env.
            _ => settings.EnvSmtpEnabled
                ? services.GetRequiredService<SmtpEmailSender>()
                : services.GetRequiredService<NoOpEmailSender>()
        };
    }
}
