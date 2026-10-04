using System.Text;
#if MOTELY_FILESYSTEM
using Bootsharp.FileSystem;
#endif

#if MOTELY_FILESYSTEM
/// <summary>
/// Folder access through Bootsharp.FileSystem (guide: extensions/file-system). The extension
/// imports IFileMounter from JS; AddBootsharp puts it in the container. JS calls
/// <c>fs.init(Bootsharp.FileSystem.FileMounter)</c> before <c>boot()</c>.
/// </summary>
public sealed class JamlFilesModule(IFileMounter mounter) : IJamlFiles, IFileWatcher
{
    private static readonly string[] FilterExtensions = [".jaml", ".yaml", ".yml", ".json"];
    private readonly SortedSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private string? _root;
    private IFileSystem? _fs;

    public event Action<JamlFileChange>? OnChange;

    public bool IsSupported() => true;
    public bool IsMounted() => _fs is not null;
    public string[] List() => [.. _names];

    public async Task<bool> PickFolder()
    {
        string? root = await mounter.PickRoot(new PickOptions { Id = "jaml-filters", Mode = PermissionMode.ReadWrite });
        if (root is null)
            return false;
        await Unmount();
        // Mount lists the folder's existing files through HandleFileChanges as added.
        _fs = await mounter.Mount(
            root,
            this,
            new MountOptions { Mode = PermissionMode.ReadWrite, Ignore = [".git", "node_modules"] }
        );
        _root = root;
        return true;
    }

    public async Task Unmount()
    {
        if (_root is not { } root)
            return;
        _root = null;
        _fs = null;
        _names.Clear();
        await mounter.Unmount(root);
    }

    public async Task<string> Load(string name) => Encoding.UTF8.GetString(await Fs.ReadFile(ToUri(name)));
    public Task Save(string name, string jaml) => Fs.WriteFile(ToUri(name), Encoding.UTF8.GetBytes(jaml));
    public Task Delete(string name) => Fs.DeleteFile(ToUri(name));
    public Task Rename(string fromName, string toName) => Fs.MoveFile(ToUri(fromName), ToUri(toName));

    public Task HandleFileChanges(IReadOnlyList<Change> changes)
    {
        foreach (var change in changes)
        {
            if (!change.File)
                continue;
            string? name = ToName(change.Entry.Uri);
            string? from = change.Moved ? ToName(change.FromUri) : null;
            if (name is null && from is null)
                continue;
            if (from is not null)
                _names.Remove(from);
            if (name is not null)
            {
                if (change.Type == ChangeType.Removed)
                    _names.Remove(name);
                else
                    _names.Add(name);
            }
            string kind = change.Type switch
            {
                ChangeType.Added => "added",
                ChangeType.Removed => "removed",
                ChangeType.Moved => "moved",
                _ => "modified",
            };
            OnChange?.Invoke(new JamlFileChange(kind, name ?? from!, from));
        }
        return Task.CompletedTask;
    }

    private IFileSystem Fs =>
        _fs ?? throw new InvalidOperationException("No folder is mounted: call JamlFiles.pickFolder() first.");

    private static bool IsFilter(string path) =>
        FilterExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>"sub/filter" is "/sub/filter.jaml"; a name with a filter extension keeps it.</summary>
    private static string ToUri(string name)
    {
        string path = name.Replace('\\', '/').TrimStart('/');
        return "/" + (IsFilter(path) ? path : path + ".jaml");
    }

    /// <summary>"/sub/filter.jaml" is "sub/filter"; null for files that are not filters.</summary>
    private static string? ToName(string uri)
    {
        string path = uri.TrimStart('/');
        if (path.EndsWith(".jaml", StringComparison.OrdinalIgnoreCase))
            return path[..^".jaml".Length];
        return IsFilter(path) ? path : null;
    }
}
#else
/// <summary>The build without Bootsharp.FileSystem: same surface, nothing can mount.</summary>
public sealed class JamlFilesModule : IJamlFiles
{
#pragma warning disable CS0067 // never raised here; declared so both builds export it
    public event Action<JamlFileChange>? OnChange;
#pragma warning restore CS0067

    public bool IsSupported() => false;
    public bool IsMounted() => false;
    public string[] List() => [];
    public Task<bool> PickFolder() => Task.FromException<bool>(NoFileSystem());
    public Task Unmount() => Task.CompletedTask;
    public Task<string> Load(string name) => Task.FromException<string>(NoFileSystem());
    public Task Save(string name, string jaml) => Task.FromException(NoFileSystem());
    public Task Delete(string name) => Task.FromException(NoFileSystem());
    public Task Rename(string fromName, string toName) => Task.FromException(NoFileSystem());

    private static NotSupportedException NoFileSystem() =>
        new("This motely-wasm build has no folder access (built without Bootsharp.FileSystem).");
}
#endif
