using System.Text;
using System.Text.Json;

namespace Keepr.Api.Features.Email;

/// <summary>
/// Brevo transport: <c>POST https://api.brevo.com/v3/smtp/email</c>, <c>api-key</c> header, JSON body.
/// See docs/feature-36-email-providers.md §2.1 and https://developers.brevo.com/reference/sendtransacemail.
/// </summary>
public sealed class BrevoEmailSender(HttpClient http, ResolvedEmailSettings settings)
    : HostedEmailSender(http, settings)
{
    protected override string ProviderName => "Brevo";

    protected override HttpRequestMessage BuildRequest(EmailMessage message)
    {
        // Brevo wants a structured sender and recipient list rather than RFC 5322 header strings.
        var recipient = string.IsNullOrWhiteSpace(message.ToName)
            ? (object)new { email = message.ToEmail }
            : new { email = message.ToEmail, name = message.ToName };

        var payload = JsonSerializer.Serialize(new
        {
            sender = new { name = Settings.FromName, email = Settings.FromAddress },
            to = new[] { recipient },
            subject = message.Subject,
            htmlContent = message.HtmlBody,
            textContent = message.TextBody
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("api-key", Settings.ApiKey);
        return request;
    }
}
