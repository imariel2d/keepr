using Keepr.Api.Services;

namespace Api.Tests;

/// <summary>
/// LIKE-metacharacter escaping. Search embeds a user term in a <c>%…%</c> pattern, so an
/// unescaped <c>%</c> or <c>_</c> would silently widen the match to unrelated rows.
/// </summary>
public class LikeEscapeTests
{
    [Fact]
    public void Leaves_ordinary_text_untouched()
    {
        Assert.Equal("report", LikeEscape.Escape("report"));
    }

    [Fact]
    public void Escapes_percent_so_it_is_a_literal_not_a_wildcard()
    {
        Assert.Equal("50\\% off", LikeEscape.Escape("50% off"));
    }

    [Fact]
    public void Escapes_underscore_so_it_matches_only_an_underscore()
    {
        Assert.Equal("a\\_b", LikeEscape.Escape("a_b"));
    }

    [Fact]
    public void Doubles_the_backslash_first_so_escapes_are_not_re_escaped()
    {
        // A single input backslash must become one escaped backslash, and a following '%' must be
        // escaped independently — not fold into the backslash's own escape.
        Assert.Equal("\\\\\\%", LikeEscape.Escape("\\%"));
    }
}
