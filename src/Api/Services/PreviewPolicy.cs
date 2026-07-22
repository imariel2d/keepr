namespace Keepr.Api.Services;

/// <summary>How the client should render a file inline. Null/None means "offer a download".</summary>
public enum PreviewKind
{
    None = 0,
    Image,
    Pdf,
    Video,
    Audio
}

/// <summary>
/// Decides what may be rendered in the browser, and as what.
///
/// This is an **allowlist, never a blocklist**, because a file's stored ContentType is not
/// trustworthy: <see cref="ContentTypeSniffer"/> returns null for signatures it doesn't know and
/// the upload then falls back to the type the *client* declared. So a file can claim to be
/// text/html. Anything not explicitly listed here is treated as "download only".
/// </summary>
public static class PreviewPolicy
{
    /// <summary>
    /// The canonical type each entry is served as. Presigned preview URLs override the response
    /// Content-Type with this value rather than passing the stored one through — so a file that
    /// lies about being a PDF is still served (and refused) as application/pdf, and cannot become
    /// active content inside the viewer's iframe.
    /// </summary>
    private static readonly Dictionary<string, (PreviewKind Kind, string ContentType)> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Images render in <img>, which never executes embedded script — that is what makes
            // SVG safe here. It must never be rendered via <iframe>/<object>, where it would.
            ["image/png"] = (PreviewKind.Image, "image/png"),
            ["image/jpeg"] = (PreviewKind.Image, "image/jpeg"),
            ["image/gif"] = (PreviewKind.Image, "image/gif"),
            ["image/webp"] = (PreviewKind.Image, "image/webp"),
            ["image/bmp"] = (PreviewKind.Image, "image/bmp"),
            ["image/svg+xml"] = (PreviewKind.Image, "image/svg+xml"),

            // The one type rendered in an iframe, hence the forced content type above.
            ["application/pdf"] = (PreviewKind.Pdf, "application/pdf"),

            ["video/mp4"] = (PreviewKind.Video, "video/mp4"),
            ["video/webm"] = (PreviewKind.Video, "video/webm"),
            ["video/x-msvideo"] = (PreviewKind.Video, "video/x-msvideo"),

            ["audio/mpeg"] = (PreviewKind.Audio, "audio/mpeg"),
            ["audio/wav"] = (PreviewKind.Audio, "audio/wav"),
            ["audio/ogg"] = (PreviewKind.Audio, "audio/ogg"),
        };

    /// <summary>How to render this type, or <see cref="PreviewKind.None"/> if it isn't previewable.</summary>
    public static PreviewKind KindOf(string? contentType) =>
        contentType is not null && Allowed.TryGetValue(Normalise(contentType), out var entry)
            ? entry.Kind
            : PreviewKind.None;

    /// <summary>The type an inline URL must be served as, or null when the type isn't previewable.</summary>
    public static string? ServeAs(string? contentType) =>
        contentType is not null && Allowed.TryGetValue(Normalise(contentType), out var entry)
            ? entry.ContentType
            : null;

    /// <summary>Lowercase JSON-friendly name for the API, or null when not previewable.</summary>
    public static string? KindName(string? contentType)
    {
        var kind = KindOf(contentType);
        return kind == PreviewKind.None ? null : kind.ToString().ToLowerInvariant();
    }

    /// <summary>Drops any parameters ("image/jpeg; charset=utf-8") and surrounding whitespace.</summary>
    private static string Normalise(string contentType)
    {
        var semicolon = contentType.IndexOf(';');
        return (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim();
    }
}
