using System.Net.Http.Headers;
using System.Text;

namespace Keepr.Api.Features.Email;

/// <summary>
/// Mailgun transport: <c>POST https://api.mailgun.net/v3/{domain}/messages</c> (US) or
/// <c>https://api.eu.mailgun.net/...</c> (EU), HTTP Basic auth (<c>api:{key}</c>), multipart/form-data
/// body. Mailgun uses a <b>fixed regional base URL</b>, never a region subdomain, and rejects
/// non-multipart bodies. See docs/feature-36-email-providers.md §2.1 and
/// https://documentation.mailgun.com/docs/mailgun/api-reference/send/mailgun/messages/post-v3--domain-name--messages.
/// </summary>
public sealed class MailgunEmailSender(HttpClient http, ResolvedEmailSettings settings)
    : HostedEmailSender(http, settings)
{
    protected override string ProviderName => "Mailgun";

    protected override HttpRequestMessage BuildRequest(EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(Settings.MailgunDomain))
            throw new InvalidOperationException("Mailgun is selected but no sending domain is configured.");

        // Fixed regional base URLs — the region selects one of a pair, it is never interpolated into
        // the hostname. Anything other than 'eu' resolves to US (the default region).
        var baseUrl = string.Equals(Settings.MailgunRegion, "eu", StringComparison.OrdinalIgnoreCase)
            ? "https://api.eu.mailgun.net"
            : "https://api.mailgun.net";

        var content = new MultipartFormDataContent
        {
            { new StringContent(From()), "from" },
            { new StringContent(message.ToEmail), "to" },
            { new StringContent(message.Subject), "subject" },
            { new StringContent(message.HtmlBody), "html" },
            { new StringContent(message.TextBody), "text" }
        };

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"{baseUrl}/v3/{Settings.MailgunDomain}/messages")
        {
            Content = content
        };
        // HTTP Basic: username "api", password the API key.
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{Settings.ApiKey}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return request;
    }
}
