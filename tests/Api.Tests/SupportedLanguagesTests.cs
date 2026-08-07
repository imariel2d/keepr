using Keepr.Api.Features.Localization;

namespace Api.Tests;

/// <summary>
/// The pure validation behind an account's preferred language (#30): which codes are accepted, how a
/// raw value is normalized, and that a blank value means "unset → default" rather than an error.
/// The endpoint wiring (PATCH /api/me/profile → 400 invalid_language) is exercised against the
/// dockerised stack. See docs/feature-30-localization.md §3.
/// </summary>
public class SupportedLanguagesTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    public void Supported_codes_are_accepted(string code)
    {
        Assert.True(SupportedLanguages.TryNormalize(code, out var normalized));
        Assert.Equal(code, normalized);
    }

    [Theory]
    [InlineData("EN", "en")]
    [InlineData("  Fr  ", "fr")]
    [InlineData("eS", "es")]
    public void Value_is_trimmed_and_lowercased(string raw, string expected)
    {
        Assert.True(SupportedLanguages.TryNormalize(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_means_unset_and_normalizes_to_null(string? raw)
    {
        // A cleared preference is valid — it falls back to the default (English), not a 400.
        Assert.True(SupportedLanguages.TryNormalize(raw, out var normalized));
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en-US")]
    [InlineData("english")]
    [InlineData("zz")]
    public void Unsupported_non_blank_value_is_rejected(string raw)
    {
        Assert.False(SupportedLanguages.TryNormalize(raw, out _));
    }

    [Fact]
    public void Default_is_english_and_is_itself_supported()
    {
        Assert.Equal("en", SupportedLanguages.Default);
        Assert.True(SupportedLanguages.IsSupported(SupportedLanguages.Default));
    }
}
