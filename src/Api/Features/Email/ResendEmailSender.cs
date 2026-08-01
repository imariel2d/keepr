using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Keepr.Api.Features.Email;

/// <summary>
/// Resend transport: <c>POST https://api.resend.com/emails</c>, Bearer auth, JSON body. See
/// docs/feature-36-email-providers.md §2.1 and https://resend.com/docs/api-reference/emails/send-email.
/// </summary>
public sealed class ResendEmailSender(HttpClient http, ResolvedEmailSettings settings)
    : HostedEmailSender(http, settings)
{
    protected override string ProviderName => "Resend";

    protected override HttpRequestMessage BuildRequest(EmailMessage message)
    {
        var payload = JsonSerializer.Serialize(new
        {
            from = From(),
            to = new[] { message.ToEmail },
            subject = message.Subject,
            html = message.HtmlBody,
            text = message.TextBody
        });

        return new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey) },
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }
}
