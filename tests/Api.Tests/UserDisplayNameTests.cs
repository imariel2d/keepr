using Keepr.Api.Domain;

namespace Api.Tests;

/// <summary>
/// <see cref="User.DisplayName"/> — how an optional first/last name becomes a single display name, or
/// null when there's nothing to show. This is the rule behind the invite email's inviter line, which
/// must name a person and never fall back to an email address. See docs/testing-strategy.md.
/// </summary>
public class UserDisplayNameTests
{
    [Theory]
    [InlineData("Jane", "Doe", "Jane Doe")]
    [InlineData("Jane", null, "Jane")]
    [InlineData(null, "Doe", "Doe")]
    [InlineData("Jane", "", "Jane")]
    [InlineData("", "Doe", "Doe")]
    [InlineData("  Jane  ", "Doe", "Jane   Doe")] // inner spacing is left as-is; only the ends trim
    public void Joins_whatever_parts_are_present(string? first, string? last, string expected)
    {
        Assert.Equal(expected, User.DisplayName(first, last));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", null)]
    [InlineData("  ", "  ")]
    public void Is_null_when_there_is_no_name(string? first, string? last)
    {
        // A blank result must be null, not "" — the caller uses null to pick the generic invite line.
        Assert.Null(User.DisplayName(first, last));
    }
}
