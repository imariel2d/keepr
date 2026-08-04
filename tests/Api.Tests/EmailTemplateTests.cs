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
        // invitedBy is now the inviter's display name (first/last), not their email.
        var content = EmailTemplates.Invite(ClaimUrl, invitedBy: "Jane Doe", expiryDays: 7);
        Assert.Contains("Jane Doe has invited you", content.HtmlBody);
        Assert.Contains("Jane Doe has invited you", content.TextBody);
    }

    [Fact]
    public void Invite_falls_back_to_a_generic_line_when_no_inviter_is_given()
    {
        // A null/blank inviter must not leave a dangling "(...)" placeholder — the template uses a
        // generic invite sentence instead. See docs/feature-36-email-providers.md §10.
        var content = EmailTemplates.Invite(ClaimUrl, invitedBy: null, expiryDays: 7);
        // Apostrophe-free substring so it matches the HTML body too (HtmlEncode escapes the ').
        Assert.Contains("been invited to Keepr", content.HtmlBody);
        Assert.Contains("been invited to Keepr", content.TextBody);
    }

    [Fact]
    public void Invite_html_encodes_untrusted_inviter_text()
    {
        // A crafted inviter value must not land as live markup in the HTML body.
        var content = EmailTemplates.Invite(ClaimUrl, invitedBy: "<script>x</script>", expiryDays: 7);

        Assert.DoesNotContain("<script>", content.HtmlBody);
        Assert.Contains("&lt;script&gt;", content.HtmlBody);
    }

    private const string ResetUrl = "https://keepr.app/reset-password/abc123";

    [Fact]
    public void Reset_includes_the_reset_url_in_both_bodies()
    {
        var content = EmailTemplates.PasswordReset(ResetUrl, expiryMinutes: 60);

        Assert.Contains(ResetUrl, content.HtmlBody);
        Assert.Contains(ResetUrl, content.TextBody);
        Assert.False(string.IsNullOrWhiteSpace(content.Subject));
    }

    [Fact]
    public void Reset_states_the_expiry_in_minutes()
    {
        Assert.Contains("60 minutes", EmailTemplates.PasswordReset(ResetUrl, 60).TextBody);
        // Singular is not "1 minutes".
        Assert.Contains("1 minute", EmailTemplates.PasswordReset(ResetUrl, 1).TextBody);
        Assert.DoesNotContain("1 minutes", EmailTemplates.PasswordReset(ResetUrl, 1).TextBody);
    }

    [Fact]
    public void Reset_reassures_the_reader_they_can_ignore_it()
    {
        // The "you can ignore this" line is what makes an unsolicited reset email non-alarming.
        var content = EmailTemplates.PasswordReset(ResetUrl, 60);
        Assert.Contains("ignore this email", content.TextBody);
        Assert.Contains("ignore this email", content.HtmlBody);
    }

    private const string ConfirmUrl = "https://keepr.app/confirm-email/abc123";

    [Fact]
    public void ConfirmEmailChange_includes_the_confirm_url_in_both_bodies()
    {
        var content = EmailTemplates.ConfirmEmailChange(ConfirmUrl, expiryMinutes: 1440);

        Assert.Contains(ConfirmUrl, content.HtmlBody);
        Assert.Contains(ConfirmUrl, content.TextBody);
        Assert.False(string.IsNullOrWhiteSpace(content.Subject));
    }

    [Fact]
    public void ConfirmEmailChange_states_the_expiry_in_hours_when_it_divides_evenly()
    {
        // 1440 minutes must read "24 hours", not "1440 minutes"; 60 → "1 hour"; a non-hour value
        // falls back to minutes.
        Assert.Contains("24 hours", EmailTemplates.ConfirmEmailChange(ConfirmUrl, 1440).TextBody);
        Assert.Contains("1 hour", EmailTemplates.ConfirmEmailChange(ConfirmUrl, 60).TextBody);
        Assert.DoesNotContain("1 hours", EmailTemplates.ConfirmEmailChange(ConfirmUrl, 60).TextBody);
        Assert.Contains("90 minutes", EmailTemplates.ConfirmEmailChange(ConfirmUrl, 90).TextBody);
    }

    [Fact]
    public void ConfirmEmailChange_reassures_the_reader_they_can_ignore_it()
    {
        var content = EmailTemplates.ConfirmEmailChange(ConfirmUrl, 1440);
        Assert.Contains("ignore this email", content.TextBody);
        Assert.Contains("ignore this email", content.HtmlBody);
    }

    [Fact]
    public void EmailChanged_names_the_masked_address_and_carries_no_link()
    {
        var content = EmailTemplates.EmailChanged("a•••@example.com");

        Assert.Contains("a•••@example.com", content.HtmlBody);
        Assert.Contains("a•••@example.com", content.TextBody);
        // A heads-up has no action link — nothing to click, so no <a href> button/fallback.
        Assert.DoesNotContain("<a href", content.HtmlBody);
        Assert.Contains("contact your admin", content.TextBody);
    }
}
