// .jaml files in a folder the user picks (guide: extensions/file-system). An exported interface
// module (guide: interop-modules); Bootsharp.Inject hands it IFileMounter (guide:
// dependency-injection). Built with Bootsharp.FileSystem by default; -p:MotelyFileSystem=false
// builds without it, and JamlFiles keeps the same surface with isSupported() false.

using System.Text;
#if MOTELY_FILESYSTEM
using Bootsharp.FileSystem;
#endif

/// <summary>.jaml files in a folder the user picks. Needs the Bootsharp.FileSystem build; without
/// it <see cref="IsSupported"/> is false and every file call rejects.</summary>
public interface IJamlFiles
{
    /// <summary>A filter file joined, left, changed or moved within the folder's filter list.</summary>
    event Action<JamlFileChange> OnChange;

    /// <summary>True when this build has Bootsharp.FileSystem and can mount a folder.</summary>
    bool IsSupported();

    /// <summary>True while a picked folder is mounted.</summary>
    bool IsMounted();

    /// <summary>Every filter file under the folder, sorted.</summary>
    string[] List();

    /// <summary>Asks the user for a folder. False when they cancel.</summary>
    Task<bool> PickFolder();

    /// <summary>Releases the mounted folder and clears the list. Nothing when none is mounted.</summary>
    Task Unmount();

    /// <summary>The filter file's text.</summary>
    /// <param name="name">Name as <see cref="List"/> gives it.</param>
    Task<string> Load(string name);

    /// <summary>Writes the filter file, creating it when missing.</summary>
    /// <param name="name">Name as <see cref="List"/> gives it.</param>
    /// <param name="jaml">The filter text.</param>
    Task Save(string name, string jaml);

    /// <summary>Deletes the filter file.</summary>
    /// <param name="name">Name as <see cref="List"/> gives it.</param>
    Task Delete(string name);

    /// <summary>Moves the filter file to a new name.</summary>
    /// <param name="fromName">Current name.</param>
    /// <param name="toName">New name.</param>
    Task Rename(string fromName, string toName);
}

/// <summary>One change to the folder's filter list.</summary>
/// <param name="Kind">added, removed, modified or moved, as the filter list sees it: a file moved
/// to a non-filter name is removed, one moved in from a non-filter name is added.</param>
/// <param name="Name">What <see cref="IJamlFiles.Load"/> takes: <c>sub/filter</c> for
/// <c>sub/filter.jaml</c>, other filter extensions kept.</param>
/// <param name="FromName">The previous name when Kind is moved, otherwise null.</param>
public readonly record struct JamlFileChange(string Kind, string Name, string? FromName);

#if MOTELY_FILESYSTEM
/// <summary>JamlFiles over Bootsharp.FileSystem. JS calls
/// <c>fs.init(Bootsharp.FileSystem.FileMounter)</c> from <c>@rewaffle/bootsharp-file-system</c>
/// before <c>boot()</c>.</summary>
/// <param name="mounter">Bootsharp.FileSystem's mounter, injected by AddBootsharp.</param>
public sealed class JamlFilesModule(IFileMounter mounter) : IJamlFiles, IFileWatcher
{
    private static readonly string[] FilterExtensions = [".jaml", ".yaml", ".yml", ".json"];
    private readonly SortedSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private string? _root;
    private IFileSystem? _fs;

    /// <inheritdoc/>
    public event Action<JamlFileChange>? OnChange;

    /// <inheritdoc/>
    public bool IsSupported() => true;

    /// <inheritdoc/>
    public bool IsMounted() => _fs is not null;

    /// <inheritdoc/>
    public string[] List() => [.. _names];

    /// <inheritdoc/>
    public async Task<bool> PickFolder()
    {
        string? root = await mounter.PickRoot(new PickOptions { Id = "jaml-filters", Mode = PermissionMode.ReadWrite });
        if (root is null)
            return false;
        await Unmount();
        // Mount reports the folder's existing files through HandleFileChanges as added.
        _fs = await mounter.Mount(
            root,
            this,
            new MountOptions { Mode = PermissionMode.ReadWrite, Ignore = [".git", "node_modules"] }
        );
        _root = root;
        return true;
    }

    /// <inheritdoc/>
    public async Task Unmount()
    {
        if (_root is not { } root)
            return;
        _root = null;
        _fs = null;
        _names.Clear();
        await mounter.Unmount(root);
    }

    /// <inheritdoc/>
    public async Task<string> Load(string name) => Encoding.UTF8.GetString(await Fs.ReadFile(ToUri(name)));

    /// <inheritdoc/>
    public Task Save(string name, string jaml) => Fs.WriteFile(ToUri(name), Encoding.UTF8.GetBytes(jaml));

    /// <inheritdoc/>
    public Task Delete(string name) => Fs.DeleteFile(ToUri(name));

    /// <inheritdoc/>
    public Task Rename(string fromName, string toName) => Fs.MoveFile(ToUri(fromName), ToUri(toName));

    /// <summary>Keeps <see cref="List"/> in step with the folder and raises <see cref="OnChange"/>.</summary>
    /// <param name="changes">The folder's changes, from Bootsharp.FileSystem.</param>
    /// <returns>Done.</returns>
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
                if (change.Removed)
                    _names.Remove(name);
                else
                    _names.Add(name);
            }
            // Kinds describe the filter list, not the disk.
            string kind = change.Type switch
            {
                ChangeType.Moved when name is null => "removed",
                ChangeType.Moved when from is null => "added",
                ChangeType.Added => "added",
                ChangeType.Removed => "removed",
                ChangeType.Moved => "moved",
                _ => "modified",
            };
            OnChange?.Invoke(new JamlFileChange(kind, name ?? from!, kind == "moved" ? from : null));
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
/// <summary>The build without Bootsharp.FileSystem: the same surface, and nothing can mount.</summary>
public sealed class JamlFilesModule : IJamlFiles
{
#pragma warning disable CS0067 // never raised here; declared so both builds export it
    /// <inheritdoc/>
    public event Action<JamlFileChange>? OnChange;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public bool IsSupported() => false;

    /// <inheritdoc/>
    public bool IsMounted() => false;

    /// <inheritdoc/>
    public string[] List() => [];

    /// <inheritdoc/>
    public Task<bool> PickFolder() => Task.FromException<bool>(NoFileSystem());

    /// <inheritdoc/>
    public Task Unmount() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<string> Load(string name) => Task.FromException<string>(NoFileSystem());

    /// <inheritdoc/>
    public Task Save(string name, string jaml) => Task.FromException(NoFileSystem());

    /// <inheritdoc/>
    public Task Delete(string name) => Task.FromException(NoFileSystem());

    /// <inheritdoc/>
    public Task Rename(string fromName, string toName) => Task.FromException(NoFileSystem());

    /// <summary>Thrown, then caught: the throw is what records the message for Errors.last().</summary>
    private static NotSupportedException NoFileSystem()
    {
        try
        {
            throw new NotSupportedException(
                "This motely-wasm build has no folder access (built without Bootsharp.FileSystem).");
        }
        catch (NotSupportedException e)
        {
            return e;
        }
    }
}
#endif
