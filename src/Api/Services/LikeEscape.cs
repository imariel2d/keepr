namespace Keepr.Api.Services;

/// <summary>
/// Escapes the SQL LIKE metacharacters so user- or name-derived text can be embedded in a pattern
/// as a literal. Pair with an explicit escape char at the call site:
/// <c>EF.Functions.Like(col, "%" + LikeEscape.Escape(term) + "%", "\\")</c>.
/// </summary>
/// <remarks>
/// Order matters: the backslash must be doubled first, otherwise the backslashes introduced when
/// escaping <c>%</c> and <c>_</c> would themselves be doubled.
/// </remarks>
public static class LikeEscape
{
    public static string Escape(string input) =>
        input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
