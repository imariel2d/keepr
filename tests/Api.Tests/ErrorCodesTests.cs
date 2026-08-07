using System.Reflection;
using System.Text.RegularExpressions;
using Keepr.Api.Http;

namespace Api.Tests;

/// <summary>
/// Guards the error-code registry's contract (#30): the <c>code</c> the API attaches to a
/// problem+json error is the client's translation key, so the string <b>values</b> must be unique
/// (two constants sharing a value would silently collide in the client's <c>ERROR_MESSAGES</c> map)
/// and stable, lowercase snake_case tokens. See docs/feature-30-localization.md §5.
/// </summary>
public class ErrorCodesTests
{
    private static readonly string[] Values =
        typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

    [Fact]
    public void There_are_codes_to_check()
    {
        // Sanity: reflection actually found the constants (guards a refactor that moves/renames them).
        Assert.True(Values.Length >= 30, $"expected the full registry, found {Values.Length}");
    }

    [Fact]
    public void Every_code_value_is_unique()
    {
        var duplicates = Values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.True(duplicates.Length == 0, $"duplicate code values: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Every_code_is_lowercase_snake_case()
    {
        foreach (var code in Values)
            Assert.Matches(new Regex("^[a-z][a-z0-9_]*$"), code);
    }
}
