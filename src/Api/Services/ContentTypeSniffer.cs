namespace Keepr.Api.Services;

/// <summary>
/// Detects a file's real content type from its leading "magic bytes" rather than trusting the
/// client's declared type or extension. See docs/ai-design-decisions.md (D6).
/// Returns null when the signature is unrecognized (caller keeps the declared type as fallback).
/// </summary>
public static class ContentTypeSniffer
{
    /// <summary>Enough bytes to cover the signatures below (some need an offset of 8).</summary>
    public const int BytesNeeded = 16;

    public static string? Detect(ReadOnlySpan<byte> b)
    {
        // Images
        if (StartsWith(b, 0x89, 0x50, 0x4E, 0x47)) return "image/png";
        if (StartsWith(b, 0xFF, 0xD8, 0xFF)) return "image/jpeg";
        if (StartsWith(b, 0x47, 0x49, 0x46, 0x38)) return "image/gif";           // GIF8
        if (StartsWith(b, 0x42, 0x4D)) return "image/bmp";                        // BM
        if (Ascii(b, 0, "RIFF") && Ascii(b, 8, "WEBP")) return "image/webp";

        // RIFF containers (share the "RIFF" prefix, differ at offset 8)
        if (Ascii(b, 0, "RIFF") && Ascii(b, 8, "AVI ")) return "video/x-msvideo";
        if (Ascii(b, 0, "RIFF") && Ascii(b, 8, "WAVE")) return "audio/wav";

        // Video / audio containers
        if (Ascii(b, 4, "ftyp")) return "video/mp4";                              // mp4/mov family
        if (StartsWith(b, 0x1A, 0x45, 0xDF, 0xA3)) return "video/webm";           // EBML: webm/mkv
        if (Ascii(b, 0, "OggS")) return "audio/ogg";
        if (Ascii(b, 0, "ID3")) return "audio/mpeg";                              // MP3 with ID3 tag
        if (b.Length >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return "audio/mpeg"; // MP3 frame sync

        // Documents / archives
        if (Ascii(b, 0, "%PDF")) return "application/pdf";
        if (StartsWith(b, 0x50, 0x4B, 0x03, 0x04)) return "application/zip";      // also docx/xlsx/pptx

        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> b, params byte[] sig)
    {
        if (b.Length < sig.Length) return false;
        for (var i = 0; i < sig.Length; i++)
            if (b[i] != sig[i]) return false;
        return true;
    }

    private static bool Ascii(ReadOnlySpan<byte> b, int offset, string text)
    {
        if (b.Length < offset + text.Length) return false;
        for (var i = 0; i < text.Length; i++)
            if (b[offset + i] != (byte)text[i]) return false;
        return true;
    }
}
