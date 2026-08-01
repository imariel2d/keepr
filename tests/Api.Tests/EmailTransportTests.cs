using System.Net;
using System.Text;
using Keepr.Api.Domain;
using Keepr.Api.Features.Email;

namespace Api.Tests;

/// <summary>
/// The hosted transports each build one provider-specific HTTP request. These lock the exact shape
/// (URL, auth header, body encoding) that the provider expects, so a wrong host or a form-vs-JSON
/// mixup is caught here rather than as a silent delivery failure. See
/// docs/feature-36-email-providers.md §2.1.
/// </summary>
public class EmailTransportTests
{
    private static readonly EmailMessage Message =
        new("to@example.com", "", "Subject line", "<p>Hello</p>", "Hello");

    private static ResolvedEmailSettings Settings(
        EmailProvider provider, string? key = "secret-key", string? domain = null, string? region = null) =>
        new(provider, "no-reply@keepr.app", "Keepr", key, domain, region, "https://keepr.app", 7);

    // --- Resend --------------------------------------------------------------------------------

    [Fact]
    public async Task Resend_posts_bearer_json_to_resend()
    {
        var handler = new CapturingHandler();
        var sender = new ResendEmailSender(new HttpClient(handler), Settings(EmailProvider.Resend));

        await sender.SendAsync(Message, default);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("secret-key", handler.Request.Headers.Authorization.Parameter);
        Assert.StartsWith("application/json", handler.ContentType);
        Assert.Contains("\"from\":\"Keepr \\u003Cno-reply@keepr.app\\u003E\"", handler.Body);
        Assert.Contains("\"to\":[\"to@example.com\"]", handler.Body);
        Assert.Contains("\"subject\":\"Subject line\"", handler.Body);
    }

    // --- Brevo ---------------------------------------------------------------------------------

    [Fact]
    public async Task Brevo_posts_api_key_header_and_structured_json()
    {
        var handler = new CapturingHandler();
        var sender = new BrevoEmailSender(new HttpClient(handler), Settings(EmailProvider.Brevo));

        await sender.SendAsync(Message, default);

        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.Request!.RequestUri!.ToString());
        Assert.Null(handler.Request.Headers.Authorization); // Brevo authenticates via a custom header
        Assert.Equal("secret-key", handler.Request.Headers.GetValues("api-key").Single());
        Assert.StartsWith("application/json", handler.ContentType);
        Assert.Contains("\"sender\":{\"name\":\"Keepr\",\"email\":\"no-reply@keepr.app\"}", handler.Body);
        Assert.Contains("\"htmlContent\":\"\\u003Cp\\u003EHello\\u003C/p\\u003E\"", handler.Body);
    }

    // --- Mailgun -------------------------------------------------------------------------------

    [Theory]
    [InlineData("us", "https://api.mailgun.net/v3/mg.keepr.app/messages")]
    [InlineData("eu", "https://api.eu.mailgun.net/v3/mg.keepr.app/messages")]
    [InlineData("US", "https://api.mailgun.net/v3/mg.keepr.app/messages")]  // case-insensitive
    [InlineData(null, "https://api.mailgun.net/v3/mg.keepr.app/messages")]  // default = US
    public async Task Mailgun_uses_the_fixed_regional_base_url(string? region, string expectedUrl)
    {
        var handler = new CapturingHandler();
        var sender = new MailgunEmailSender(
            new HttpClient(handler), Settings(EmailProvider.Mailgun, domain: "mg.keepr.app", region: region));

        await sender.SendAsync(Message, default);

        Assert.Equal(expectedUrl, handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Mailgun_posts_basic_auth_multipart()
    {
        var handler = new CapturingHandler();
        var sender = new MailgunEmailSender(
            new HttpClient(handler), Settings(EmailProvider.Mailgun, domain: "mg.keepr.app", region: "us"));

        await sender.SendAsync(Message, default);

        Assert.Equal("Basic", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("api:secret-key")),
            handler.Request.Headers.Authorization.Parameter);
        Assert.StartsWith("multipart/form-data", handler.ContentType);
        // The multipart body carries the fields by name and value (disposition name is unquoted).
        Assert.Contains("name=from", handler.Body);
        Assert.Contains("Keepr <no-reply@keepr.app>", handler.Body);
        Assert.Contains("name=to", handler.Body);
        Assert.Contains("to@example.com", handler.Body);
    }

    [Fact]
    public async Task Mailgun_without_a_domain_throws()
    {
        var sender = new MailgunEmailSender(
            new HttpClient(new CapturingHandler()), Settings(EmailProvider.Mailgun, domain: null, region: "us"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message, default));
    }

    // --- Shared behaviour ----------------------------------------------------------------------

    [Fact]
    public async Task A_missing_api_key_throws_before_sending()
    {
        var handler = new CapturingHandler();
        var sender = new ResendEmailSender(new HttpClient(handler), Settings(EmailProvider.Resend, key: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message, default));
        Assert.Null(handler.Request); // never hit the wire
    }

    [Fact]
    public async Task A_non_2xx_provider_response_throws()
    {
        var handler = new CapturingHandler { Status = HttpStatusCode.Unauthorized, ResponseBody = "bad key" };
        var sender = new ResendEmailSender(new HttpClient(handler), Settings(EmailProvider.Resend));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(Message, default));
        Assert.Contains("Resend", ex.Message);
        Assert.Contains("401", ex.Message);
    }

    /// <summary>Captures the outgoing request (and its body) and returns a canned response.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public string? ContentType { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
                ContentType = request.Content.Headers.ContentType?.ToString();
            }
            return new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody) };
        }
    }
}
