using System.Net;
using System.Text;

namespace Keepr.Api.Features.Email;

/// <summary>A rendered email: subject plus both body representations.</summary>
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Server-side email rendering, Cove's look re-expressed with email-safe primitives. Mail clients
/// (Outlook especially) don't support CSS variables, flexbox/grid, external webfonts, or
/// <c>@media(prefers-color-scheme)</c> reliably, so this does <b>not</b> import the app's CSS — it
/// hand-writes a table layout with inlined literal hex colors (the actual values from
/// <c>tokens.css</c>) and a web-safe font stack. A plain-<c>StringBuilder</c> templater rather than
/// Razor: three fields don't warrant the dependency. See docs/feature-36-account-provisioning.md §10.
/// </summary>
public static class EmailTemplates
{
    // Cove palette, literal (custom properties don't resolve in mail clients). See tokens.css.
    private const string Paper = "#FBF9F6";     // --gray-25  page canvas
    private const string Card = "#FFFFFF";      // --surface-card
    private const string BorderSubtle = "#EDE7DD"; // --gray-100
    private const string Ink = "#221D18";       // --gray-900 text-primary
    private const string InkSoft = "#695D4E";   // --gray-600 text-secondary
    private const string InkFaint = "#A3937A";  // --gray-400 text-tertiary
    private const string Terracotta = "#E8703A"; // --brand-500 accent
    private const string TerracottaDark = "#B04519"; // --brand-700 (button border for depth)

    // Web-safe cousins of Sora/Manrope — mail clients drop the Google Fonts import.
    private const string FontStack =
        "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";

    /// <summary>The invite email: "you've been invited", a Set-your-password button to the claim
    /// link, the raw link as a fallback, and the expiry. <paramref name="invitedBy"/> is the inviter's
    /// display name (their first/last name), not their email — when it's null/blank the template uses
    /// a generic line rather than exposing any address.</summary>
    public static EmailContent Invite(string claimUrl, string? invitedBy, int expiryDays)
    {
        var by = string.IsNullOrWhiteSpace(invitedBy) ? null : invitedBy;
        var expiry = expiryDays == 1 ? "1 day" : $"{expiryDays} days";

        var intro = by is null
            ? "You've been invited to Keepr, a private place to keep your files."
            : $"{by} has invited you to Keepr, a private place to keep your files.";

        var bodyHtml =
            Paragraph(intro) +
            Paragraph("Set a password to activate your account and sign in.");

        var html = Layout(
            preheader: "Set your password to activate your Keepr account.",
            headline: "You're invited to Keepr",
            bodyHtml: bodyHtml,
            ctaText: "Set your password",
            ctaUrl: claimUrl,
            footerNote: $"This link expires in {expiry}. If the button doesn't work, paste this "
                        + "address into your browser:");

        var text = new StringBuilder()
            .AppendLine("You're invited to Keepr")
            .AppendLine()
            .AppendLine(intro)
            .AppendLine()
            .AppendLine("Set a password to activate your account and sign in:")
            .AppendLine(claimUrl)
            .AppendLine()
            .AppendLine($"This link expires in {expiry}.")
            .ToString();

        return new EmailContent("You're invited to Keepr", html, text);
    }

    /// <summary>The password-reset email: a Choose-a-new-password button to the reset link, the raw
    /// link as a fallback, the expiry, and a "you can ignore this" reassurance. Carries no account
    /// detail beyond the link itself. <paramref name="expiryMinutes"/> is the link lifetime (§4).</summary>
    public static EmailContent PasswordReset(string resetUrl, int expiryMinutes)
    {
        var minutes = expiryMinutes == 1 ? "1 minute" : $"{expiryMinutes} minutes";

        var bodyHtml =
            Paragraph("We received a request to reset the password on your Keepr account.") +
            Paragraph("Choose a new password to finish. If you didn't ask for this, you can ignore "
                      + "this email — your password won't change.");

        var html = Layout(
            preheader: "Choose a new password for your Keepr account.",
            headline: "Reset your password",
            bodyHtml: bodyHtml,
            ctaText: "Choose a new password",
            ctaUrl: resetUrl,
            footerNote: $"This link expires in {minutes}. If the button doesn't work, paste this "
                        + "address into your browser:");

        var text = new StringBuilder()
            .AppendLine("Reset your Keepr password")
            .AppendLine()
            .AppendLine("We received a request to reset the password on your Keepr account.")
            .AppendLine("Choose a new password to finish:")
            .AppendLine(resetUrl)
            .AppendLine()
            .AppendLine($"This link expires in {minutes}.")
            .AppendLine("If you didn't request this, you can ignore this email — your password won't change.")
            .ToString();

        return new EmailContent("Reset your Keepr password", html, text);
    }

    /// <summary>
    /// The shared Cove-styled shell: centered ~600px card on the paper canvas, wordmark, headline,
    /// body, a bulletproof button, and a footer with the raw link. A dark-mode block is included as
    /// progressive enhancement (Apple/iOS Mail honor it); the light design is the standalone baseline.
    /// </summary>
    private static string Layout(
        string preheader, string headline, string bodyHtml,
        string ctaText, string ctaUrl, string footerNote)
    {
        var url = HtmlAttr(ctaUrl);
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta name="color-scheme" content="light dark">
  <title>{{HtmlText(headline)}}</title>
  <style>
    @media (prefers-color-scheme: dark) {
      .kp-body { background: #16120F !important; }
      .kp-card { background: #362F28 !important; border-color: #4A4038 !important; }
      .kp-ink { color: #F4EFE7 !important; }
      .kp-ink-soft { color: #C4B8A2 !important; }
      .kp-faint { color: #857562 !important; }
    }
  </style>
</head>
<body class="kp-body" style="margin:0; padding:0; background:{{Paper}};">
  <!-- preheader: shown in the inbox preview, hidden in the body -->
  <div style="display:none; max-height:0; overflow:hidden; opacity:0;">{{HtmlText(preheader)}}</div>
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{{Paper}};">
    <tr>
      <td align="center" style="padding:32px 16px;">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px; max-width:100%;">
          <tr>
            <td style="padding:8px 4px 20px 4px;">
              <span style="font-family:{{FontStack}}; font-size:22px; font-weight:800; letter-spacing:-0.01em; color:{{Terracotta}};">Keepr</span>
            </td>
          </tr>
          <tr>
            <td class="kp-card" style="background:{{Card}}; border:1px solid {{BorderSubtle}}; border-radius:14px; padding:36px 36px 32px 36px;">
              <h1 class="kp-ink" style="margin:0 0 16px 0; font-family:{{FontStack}}; font-size:26px; line-height:1.2; font-weight:700; letter-spacing:-0.01em; color:{{Ink}};">{{HtmlText(headline)}}</h1>
              {{bodyHtml}}
              <table role="presentation" cellpadding="0" cellspacing="0" style="margin:28px 0 8px 0;">
                <tr>
                  <td align="center" bgcolor="{{Terracotta}}" style="border-radius:10px; background:{{Terracotta}};">
                    <a href="{{url}}" target="_blank" style="display:inline-block; padding:14px 28px; font-family:{{FontStack}}; font-size:16px; font-weight:700; color:#FFFFFF; text-decoration:none; border-radius:10px; border:1px solid {{TerracottaDark}};">{{HtmlText(ctaText)}}</a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style="padding:20px 8px 8px 8px;">
              <p class="kp-faint" style="margin:0 0 8px 0; font-family:{{FontStack}}; font-size:13px; line-height:1.5; color:{{InkFaint}};">{{HtmlText(footerNote)}}</p>
              <p style="margin:0 0 16px 0; font-family:{{FontStack}}; font-size:13px; line-height:1.5; word-break:break-all;"><a href="{{url}}" target="_blank" style="color:{{Terracotta}}; text-decoration:underline;">{{HtmlText(ctaUrl)}}</a></p>
              <p class="kp-faint" style="margin:0; font-family:{{FontStack}}; font-size:12px; line-height:1.5; color:{{InkFaint}};">If you weren't expecting this, you can ignore this email.</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    private static string Paragraph(string text) =>
        $"""<p class="kp-ink-soft" style="margin:0 0 14px 0; font-family:{FontStack}; font-size:16px; line-height:1.6; color:{InkSoft};">{HtmlText(text)}</p>""";

    // Body/attribute text comes from user-controlled data (invitee, inviter email), so encode it.
    private static string HtmlText(string value) => WebUtility.HtmlEncode(value);

    // Attribute context (href): encode quotes/angle brackets so the value can't break out of the
    // attribute. The claim URL we build is already URL-safe, but defense in depth is free.
    private static string HtmlAttr(string value) =>
        WebUtility.HtmlEncode(value).Replace("\"", "&quot;");
}
