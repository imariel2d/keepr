using Keepr.Api.Services;

namespace Api.Tests;

/// <summary>
/// Auto-suffix rules (Q-A/Q-B). These are the edge cases that silently produce wrong filenames
/// if the extension or counter handling is off — see docs/folder-hierarchy-design.md §4.0.
/// </summary>
public class NameAllocatorTests
{
    private static HashSet<string> Taken(params string[] names) =>
        names.Select(n => n.ToLowerInvariant()).ToHashSet();

    [Fact]
    public void Returns_the_requested_name_when_nothing_is_taken()
    {
        Assert.Equal("Photos", NameAllocator.Allocate("Photos", Taken(), isFile: false));
    }

    [Fact]
    public void Suffixes_a_folder_on_collision()
    {
        Assert.Equal("Photos (2)", NameAllocator.Allocate("Photos", Taken("Photos"), isFile: false));
    }

    [Fact]
    public void Skips_over_suffixes_that_are_already_used()
    {
        var taken = Taken("Photos", "Photos (2)", "Photos (3)");
        Assert.Equal("Photos (4)", NameAllocator.Allocate("Photos", taken, isFile: false));
    }

    [Fact]
    public void Puts_the_counter_before_the_extension()
    {
        // "report.pdf (2)" would break the extension the UI infers the type from.
        Assert.Equal("report (2).pdf",
            NameAllocator.Allocate("report.pdf", Taken("report.pdf"), isFile: true));
    }

    [Fact]
    public void Treats_names_case_insensitively()
    {
        Assert.Equal("BEACH (2).JPG",
            NameAllocator.Allocate("BEACH.JPG", Taken("beach.jpg"), isFile: true));
    }

    [Fact]
    public void Continues_an_existing_counter_series_instead_of_nesting()
    {
        // "Trip (2) (2)" is the bug this prevents.
        var taken = Taken("Trip", "Trip (2)");
        Assert.Equal("Trip (3)", NameAllocator.Allocate("Trip (2)", taken, isFile: false));
    }

    [Fact]
    public void Keeps_a_counter_name_as_typed_when_it_is_free()
    {
        // Only *colliding* names get normalised; "Trip (2)" must not be pulled down to "Trip".
        Assert.Equal("Trip (2)", NameAllocator.Allocate("Trip (2)", Taken("Trip"), isFile: false));
    }

    [Fact]
    public void Handles_a_file_with_no_extension()
    {
        Assert.Equal("README (2)", NameAllocator.Allocate("README", Taken("readme"), isFile: true));
    }

    [Fact]
    public void Handles_a_dotfile_as_a_name_not_an_extension()
    {
        // Path.GetExtension(".env") returns ".env", so a naive split leaves an empty stem and
        // yields " (2).env" — a leading space and a lost name.
        Assert.Equal(".env (2)", NameAllocator.Allocate(".env", Taken(".env"), isFile: true));
    }

    [Fact]
    public void Multi_dot_names_only_split_the_last_extension()
    {
        Assert.Equal("archive.tar (2).gz",
            NameAllocator.Allocate("archive.tar.gz", Taken("archive.tar.gz"), isFile: true));
    }

    [Theory]
    [InlineData("50% off.pdf", true)]
    [InlineData("under_score.txt", true)]
    [InlineData("back\\slash", false)]
    public void Series_pattern_escapes_like_wildcards(string name, bool isFile)
    {
        var pattern = NameAllocator.SeriesPattern(name, isFile);

        // Every LIKE metacharacter from the stem must arrive escaped, or the "what's taken here?"
        // query would match unrelated rows and suffix names that never actually collided.
        var stem = pattern[..pattern.IndexOf('%')];
        Assert.DoesNotContain("%", stem.Replace("\\%", ""));
        Assert.DoesNotContain("_", stem.Replace("\\_", ""));
    }

    [Fact]
    public void Series_pattern_targets_the_extension_so_unrelated_types_do_not_collide()
    {
        // "report.pdf" must not be considered taken by "report.docx".
        Assert.Equal("report%.pdf", NameAllocator.SeriesPattern("report.pdf", isFile: true));
    }
}
