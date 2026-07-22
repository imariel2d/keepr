using Keepr.Api.Services;
using Keepr.Api.Storage;

namespace Api.Tests;

/// <summary>
/// The allowlist is a security boundary, not a convenience: a file's stored ContentType is
/// whatever the client declared when the magic-byte sniffer didn't recognise the signature.
/// </summary>
public class PreviewPolicyTests
{
    [Theory]
    [InlineData("image/png", "image")]
    [InlineData("image/jpeg", "image")]
    [InlineData("image/svg+xml", "image")]
    [InlineData("application/pdf", "pdf")]
    [InlineData("video/mp4", "video")]
    [InlineData("audio/mpeg", "audio")]
    public void Allowlisted_types_get_a_preview_kind(string contentType, string expected)
    {
        Assert.Equal(expected, PreviewPolicy.KindName(contentType));
    }

    [Theory]
    [InlineData("text/html")]          // the reason this is an allowlist at all
    [InlineData("application/javascript")]
    [InlineData("image/svg")]          // near-miss of a real type; not the registered spelling
    [InlineData("application/zip")]
    [InlineData("text/plain")]
    [InlineData(null)]
    [InlineData("")]
    public void Everything_else_is_download_only(string? contentType)
    {
        Assert.Null(PreviewPolicy.KindName(contentType));
        Assert.Null(PreviewPolicy.ServeAs(contentType));
        Assert.Equal(PreviewKind.None, PreviewPolicy.KindOf(contentType));
    }

    [Fact]
    public void Svg_previews_as_an_image_so_it_renders_in_an_img_tag()
    {
        // <img> never executes script inside an SVG; <iframe>/<object> would. Classifying SVG as
        // an image is what keeps it off the iframe path.
        Assert.Equal(PreviewKind.Image, PreviewPolicy.KindOf("image/svg+xml"));
    }

    [Fact]
    public void Pdf_is_served_as_pdf_not_as_whatever_was_stored()
    {
        // The iframe path is the only one that could execute active content, so the response
        // content type is forced rather than passed through.
        Assert.Equal("application/pdf", PreviewPolicy.ServeAs("application/pdf"));
    }

    [Theory]
    [InlineData("IMAGE/PNG")]
    [InlineData("image/png; charset=binary")]
    [InlineData("  image/png  ")]
    public void Type_matching_ignores_case_parameters_and_whitespace(string contentType)
    {
        Assert.Equal("image", PreviewPolicy.KindName(contentType));
    }
}

/// <summary>
/// Content-Disposition is how the browser learns a file's real name — storage keys are opaque
/// UUIDs, so without it a download is saved as the UUID.
/// </summary>
public class PresignHeaderTests
{
    [Fact]
    public void No_filename_means_no_disposition_header()
    {
        Assert.Null(PresignHeaders.None.BuildContentDisposition());
    }

    [Fact]
    public void Download_asks_the_browser_to_save_under_the_real_name()
    {
        var header = PresignHeaders.ForDownload("beach.jpg").BuildContentDisposition();
        Assert.StartsWith("attachment; ", header);
        Assert.Contains("filename=\"beach.jpg\"", header);
    }

    [Fact]
    public void Inline_asks_the_browser_to_render_it()
    {
        var header = PresignHeaders.ForInline("beach.jpg", "image/jpeg").BuildContentDisposition();
        Assert.StartsWith("inline; ", header);
    }

    [Fact]
    public void Unicode_names_get_both_an_ascii_fallback_and_an_encoded_form()
    {
        // A bare filename="…" cannot carry non-ASCII, so RFC 6266 wants both spellings.
        var header = PresignHeaders.ForDownload("отчёт.pdf").BuildContentDisposition()!;
        Assert.Contains("filename*=UTF-8''", header);
        Assert.Contains("%D0", header);                  // percent-encoded Cyrillic
        Assert.DoesNotContain("отчёт", header[..header.IndexOf("filename*", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Quotes_are_stripped_so_they_cannot_terminate_the_header_early()
    {
        // 'evil".jpg' would otherwise close the quoted string and let the rest be reparsed.
        var header = PresignHeaders.ForDownload("evil\".jpg").BuildContentDisposition()!;
        var fallback = header["attachment; filename=\"".Length..header.IndexOf("\"; filename*", StringComparison.Ordinal)];
        Assert.DoesNotContain("\"", fallback);
    }

    [Fact]
    public void Control_characters_are_stripped()
    {
        var header = PresignHeaders.ForDownload("bad\r\nInjected: header.jpg").BuildContentDisposition()!;
        Assert.DoesNotContain("\r", header);
        Assert.DoesNotContain("\n", header);
    }

    [Fact]
    public void Non_ascii_names_become_underscores_in_the_fallback_only()
    {
        // The fallback is deliberately lossy; filename* alongside it carries the real name, and
        // every current browser prefers that one.
        var header = PresignHeaders.ForDownload("报告.pdf").BuildContentDisposition()!;
        Assert.Contains("filename=\"__.pdf\"", header);
        Assert.Contains("filename*=UTF-8''%E6%8A%A5%E5%91%8A.pdf", header);
    }

    [Fact]
    public void A_name_with_nothing_usable_falls_back_to_a_placeholder()
    {
        // Only reachable when every character was stripped outright — quotes and control
        // characters, rather than merely non-ASCII.
        Assert.Contains("filename=\"download\"", PresignHeaders.ForDownload("\"\"\"").BuildContentDisposition()!);
    }
}
