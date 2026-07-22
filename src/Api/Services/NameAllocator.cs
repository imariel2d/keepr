using System.Text.RegularExpressions;

namespace Keepr.Api.Services;

/// <summary>
/// Picks a non-colliding name by appending a counter — "Photos" → "Photos (2)" → "Photos (3)".
/// Collisions never fail a request; they rename. See docs/folder-hierarchy-design.md (Q-A, §4.0).
///
/// The result is advisory: the unique index is the referee, so callers must still retry on a
/// 23505 from a racing writer (see <see cref="NameConflict"/>).
/// </summary>
public static partial class NameAllocator
{
    /// <summary>Matches a trailing " (12)" so an existing counter continues instead of nesting.</summary>
    [GeneratedRegex(@"^(?<base>.*?)\s*\((?<n>\d+)\)$", RegexOptions.Singleline)]
    private static partial Regex TrailingCounter();

    /// <summary>Safety valve; a folder with 10k same-named siblings is pathological either way.</summary>
    private const int MaxAttempts = 10_000;

    /// <summary>
    /// First free name for <paramref name="desired"/> given the names already used in the
    /// destination (compared lowercased).
    /// </summary>
    /// <param name="desired">The name the caller asked for.</param>
    /// <param name="takenLower">Lowercased names already used in the destination.</param>
    /// <param name="isFile">
    /// True to keep the extension on the end: "report.pdf" → "report (2).pdf", never
    /// "report.pdf (2)", which would break the type the UI infers from the extension.
    /// </param>
    public static string Allocate(string desired, IReadOnlySet<string> takenLower, bool isFile)
    {
        // The requested name wins whenever it is free — including a name that already ends in a
        // counter, so "Trip (2)" stays "Trip (2)" rather than being normalised down to "Trip".
        if (!takenLower.Contains(desired.ToLowerInvariant())) return desired;

        var (stem, ext) = Split(desired, isFile);

        // Continue an existing series: colliding "Trip (2)" probes "Trip (3)", not "Trip (2) (2)".
        var match = TrailingCounter().Match(stem);
        if (match.Success) stem = match.Groups["base"].Value;

        for (var n = 2; n < MaxAttempts; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!takenLower.Contains(candidate.ToLowerInvariant())) return candidate;
        }

        // Astronomically unlikely; a suffix nobody will collide with beats throwing.
        return $"{stem} ({Guid.NewGuid():N}){ext}";
    }

    /// <summary>
    /// The LIKE pattern matching every name that could be part of <paramref name="desired"/>'s
    /// counter series, for narrowing the "what's taken here?" query to the relevant rows.
    /// </summary>
    public static string SeriesPattern(string desired, bool isFile)
    {
        var (stem, ext) = Split(desired, isFile);
        var match = TrailingCounter().Match(stem);
        if (match.Success) stem = match.Groups["base"].Value;

        // Escape LIKE wildcards so a file literally named "50%_off.pdf" doesn't match everything.
        var escaped = stem.ToLowerInvariant()
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"{escaped}%{ext.ToLowerInvariant()}";
    }

    private static (string Stem, string Ext) Split(string name, bool isFile)
    {
        if (!isFile) return (name, string.Empty);

        // A leading dot is part of the name, not an extension: Path.GetExtension(".env") returns
        // ".env", which would leave an empty stem and produce " (2).env".
        var dot = name.LastIndexOf('.');
        if (dot <= 0) return (name, string.Empty);

        return (name[..dot], name[dot..]);
    }
}
