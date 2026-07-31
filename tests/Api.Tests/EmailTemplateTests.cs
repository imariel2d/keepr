using Keepr.Api.Features.Email;

namespace Api.Tests;

/// <summary>
/// The invite email carries the claim link (button and raw fallback), the expiry, and any
/// inviter attribution — and must HTML-encode untrusted values so a crafted address can't inject
/// markup. Rendering to a real client is eyeballed separately (§10.3); this pins the content and the
/// encoding. See docs/feature-36-account-provisioning.md §10.
/// </summary>
public class EmailTemplateTests
{
    private const string ClaimUrl = "https://keepr.app/claim/abc123";

    [Fact]
    public void Invite_includes_the_claim_url_in_both_bodies()
    {
        var content = EmailTemplates.Invite(ClaimUrl, invitedBy: null, expiryDays: 7);

        Assert.Contains(ClaimUrl, content.HtmlBody);
        Assert.Contains(ClaimUrl, content.TextBody);
        Assert.False(string.IsNullOrWhiteSpace(content.Subject));
    }

    [Fact]
    public void Invite_states_the_expiry()
    {
        Assert.Contains("7 days", EmailTemplates.Invite(ClaimUrl, null, 7).TextBody);
        // Singular is not "1 days".
        Assert.Contains("1 day", EmailTemplates.Invite(ClaimUrl, null, 1).TextBody);
        Assert.DoesNotContain("1 days", EmailTemplates.Invite(ClaimUrl, null, 1).TextBody);
    }

    [Fact]
    public void Invite_names_the_inviter_when_given()
    {
        var content = EmailTemplates.Invite(ClaimUrl, invitedBy: "owner@example.com", expiryDays: 7);
        Assert.Contains("owner@example.com", content.HtmlBody);
        Assert.Contains("owner@example.com", content.TextBody);
    }

    [Fact]
    public void Invite_html_encodes_untrusted_inviter_text()
    {
        // A crafted inviter value must not land as live markup in the HTML body.
        var content = EmailTemplates.Invite(ClaimUrl, invitedBy: "<script>x</script>", expiryDays: 7);

        Assert.DoesNotContain("<script>", content.HtmlBody);
        Assert.Contains("&lt;script&gt;", content.HtmlBody);
    }
}
