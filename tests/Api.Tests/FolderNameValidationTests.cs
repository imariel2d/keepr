using Keepr.Api.Services;

namespace Api.Tests;

/// <summary>
/// Name validation is shared by folders, uploads, and rename, so a gap here reaches every entry
/// point that writes a name.
/// </summary>
public class FolderNameValidationTests
{
    [Fact]
    public void Trims_surrounding_whitespace()
    {
        Assert.Equal("Photos", FolderService.ValidateName("  Photos  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_name(string name)
    {
        Assert.Throws<FolderException>(() => FolderService.ValidateName(name));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void Rejects_path_traversal_names(string name)
    {
        Assert.Throws<FolderException>(() => FolderService.ValidateName(name));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Rejects_slashes_which_would_imply_a_path(string name)
    {
        Assert.Throws<FolderException>(() => FolderService.ValidateName(name));
    }

    [Theory]
    [InlineData("bad\tname")]
    [InlineData("bad\nname")]
    [InlineData("bad\u0000name")]
    public void Rejects_control_characters(string name)
    {
        Assert.Throws<FolderException>(() => FolderService.ValidateName(name));
    }

    [Fact]
    public void Rejects_names_longer_than_the_column()
    {
        // 255 is the column width; a longer name would be a database error rather than a 400.
        Assert.Throws<FolderException>(() => FolderService.ValidateName(new string('x', 256)));
    }

    [Fact]
    public void Accepts_a_name_at_exactly_the_limit()
    {
        Assert.Equal(255, FolderService.ValidateName(new string('x', 255)).Length);
    }
}
