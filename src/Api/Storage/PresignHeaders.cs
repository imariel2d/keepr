using System.Net;
using System.Text;

namespace Keepr.Api.Storage;

/// <summary>
/// Response headers to bake into a presigned GET, via S3's response-header overrides.
///
/// This is the only way the browser can learn a file's real name: storage keys are opaque
/// (`{ownerId}/{uuid}.jpg`) by design, so without an explicit Content-Disposition a "Save as"
/// suggests the UUID. See docs/folder-hierarchy-design.md (FD1) for why keys stay opaque.
/// </summary>
public readonly record struct PresignHeaders(
    string? FileName = null,
    bool Inline = false,
    string? ContentType = null)
{
    /// <summary>Nothing overridden — a bare presigned GET, as before.</summary>
    public static PresignHeaders None => default;

    /// <summary>Render in the page (preview). Content type is forced, never taken on trust.</summary>
    public static PresignHeaders ForInline(string fileName, string contentType) =>
        new(fileName, Inline: true, ContentType: contentType);

    /// <summary>Save to disk under the user's own filename.</summary>
    public static PresignHeaders ForDownload(string fileName) => new(fileName, Inline: false);

    /// <summary>
    /// The Content-Disposition value, or null when no filename was supplied.
    ///
    /// Filenames can hold any Unicode, which a bare <c>filename="…"</c> cannot express, so this
    /// emits both forms per RFC 6266/5987: an ASCII-only fallback for old clients and
    /// <c>filename*=UTF-8''…</c> for everyone else.
    /// </summary>
    public string? BuildContentDisposition()
    {
        if (string.IsNullOrWhiteSpace(FileName)) return null;

        var type = Inline ? "inline" : "attachment";
        var ascii = ToAsciiFallback(FileName);
        var encoded = Uri.EscapeDataString(FileName);

        return $"{type}; filename=\"{ascii}\"; filename*=UTF-8''{encoded}";
    }

    /// <summary>
    /// Non-ASCII characters become '_', and quotes/backslashes/control characters are dropped —
    /// an unescaped quote would terminate the header value early and let the rest be reinterpreted.
    /// </summary>
    private static string ToAsciiFallback(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c is '"' or '\\' || char.IsControl(c)) continue;
            sb.Append(c > 127 ? '_' : c);
        }
        var cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 ? "download" : cleaned;
    }
}
