namespace Motely.Tests;

/// <summary>
/// The browser build's filter naming (Motely.Wasm/JamlFileNames.cs, compiled into this project
/// as well): what a folder entry is called in the filter list, and what a folder change does to it.
/// </summary>
public sealed class JamlFileNamesTests
{
    [Theory]
    [InlineData("perkeo", "/perkeo.yaml")]
    [InlineData("sub/perkeo", "/sub/perkeo.yaml")]
    [InlineData("sub\\perkeo.yml", "/sub/perkeo.yml")]
    [InlineData("sub/perkeo.jaml", "/sub/perkeo.jaml")]
    [InlineData("/perkeo.yaml", "/perkeo.yaml")]
    public void A_name_is_a_yaml_file_under_the_folder(string name, string uri)
    {
        Assert.Equal(uri, JamlFileNames.ToUri(name));
    }

    [Theory]
    [InlineData("/perkeo.yaml", "perkeo")]
    [InlineData("/sub/perkeo.YAML", "sub/perkeo")]
    [InlineData("/sub/perkeo.yml", "sub/perkeo.yml")]
    [InlineData("/sub/perkeo.jaml", "sub/perkeo.jaml")]
    [InlineData("/sub/perkeo.JAML", "sub/perkeo.JAML")]
    [InlineData("/notes.txt", null)]
    [InlineData("/perkeo.jaml", "perkeo.jaml")]
    public void A_file_is_named_without_its_yaml_extension(string uri, string? name)
    {
        Assert.Equal(name, JamlFileNames.ToName(uri));
    }

    [Theory]
    [InlineData(JamlFileEvent.Added, "/a.yaml", null, "added", "a", null)]
    [InlineData(JamlFileEvent.Modified, "/a.yaml", null, "modified", "a", null)]
    [InlineData(JamlFileEvent.Removed, "/a.yaml", null, "removed", "a", null)]
    [InlineData(JamlFileEvent.Moved, "/b.yaml", "/a.yaml", "moved", "b", "a")]
    [InlineData(JamlFileEvent.Moved, "/b.txt", "/a.yaml", "removed", "a", null)]
    [InlineData(JamlFileEvent.Moved, "/b.yaml", "/a.txt", "added", "b", null)]
    public void A_folder_change_becomes_a_filter_list_change(
        JamlFileEvent type, string uri, string? fromUri, string kind, string name, string? fromName)
    {
        Assert.Equal(new JamlFileChange(kind, name, fromName), JamlFileNames.Change(type, uri, fromUri));
    }

    [Fact]
    public void A_change_to_a_non_filter_is_nothing()
    {
        Assert.Null(JamlFileNames.Change(JamlFileEvent.Added, "/notes.txt", null));
        Assert.Null(JamlFileNames.Change(JamlFileEvent.Moved, "/b.txt", "/a.txt"));
    }
}
