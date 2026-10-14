namespace Motely.Tests;

/// <summary>
/// The browser build's filter naming (Motely.Wasm/YAMLFileNames.cs, compiled into this project
/// as well): what a folder entry is called in the filter list, and what a folder change does to it.
/// </summary>
public sealed class YAMLFileNamesTests
{
    [Theory]
    [InlineData("perkeo", "/perkeo.yaml")]
    [InlineData("sub/perkeo", "/sub/perkeo.yaml")]
    [InlineData("sub\\perkeo.yml", "/sub/perkeo.yml")]
    [InlineData("/perkeo.yaml", "/perkeo.yaml")]
    public void A_name_is_a_yaml_file_under_the_folder(string name, string uri)
    {
        Assert.Equal(uri, YAMLFileNames.ToUri(name));
    }

    [Theory]
    [InlineData("/perkeo.yaml", "perkeo")]
    [InlineData("/sub/perkeo.YAML", "sub/perkeo")]
    [InlineData("/sub/perkeo.yml", "sub/perkeo.yml")]
    [InlineData("/notes.txt", null)]
    [InlineData("/perkeo.yaml", null)]
    public void A_file_is_named_without_its_yaml_extension(string uri, string? name)
    {
        Assert.Equal(name, YAMLFileNames.ToName(uri));
    }

    [Theory]
    [InlineData(YAMLFileEvent.Added, "/a.yaml", null, "added", "a", null)]
    [InlineData(YAMLFileEvent.Modified, "/a.yaml", null, "modified", "a", null)]
    [InlineData(YAMLFileEvent.Removed, "/a.yaml", null, "removed", "a", null)]
    [InlineData(YAMLFileEvent.Moved, "/b.yaml", "/a.yaml", "moved", "b", "a")]
    [InlineData(YAMLFileEvent.Moved, "/b.txt", "/a.yaml", "removed", "a", null)]
    [InlineData(YAMLFileEvent.Moved, "/b.yaml", "/a.txt", "added", "b", null)]
    public void A_folder_change_becomes_a_filter_list_change(
        YAMLFileEvent type, string uri, string? fromUri, string kind, string name, string? fromName)
    {
        Assert.Equal(new YAMLFileChange(kind, name, fromName), YAMLFileNames.Change(type, uri, fromUri));
    }

    [Fact]
    public void A_change_to_a_non_filter_is_nothing()
    {
        Assert.Null(YAMLFileNames.Change(YAMLFileEvent.Added, "/notes.txt", null));
        Assert.Null(YAMLFileNames.Change(YAMLFileEvent.Moved, "/b.txt", "/a.txt"));
    }
}
