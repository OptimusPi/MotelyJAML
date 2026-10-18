// Filter files in a folder the user picks, through Bootsharp.FileSystem (guide: extensions/file-system).
// An exported interface module (guide: interop-modules); Bootsharp.Inject hands it IFileMounter
// (guide: dependency-injection). Built with the package by default; -p:MotelyFileSystem=false
// builds the same surface with isSupported() false.

using System.Text;
#if MOTELY_FILESYSTEM
using Bootsharp.FileSystem;
#endif

/// <summary>.yaml and .yml filter files in a folder the user picks. Needs the Bootsharp.FileSystem
/// build; without it <see cref="IsSupported"/> is false and every file call rejects.</summary>
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
    /// <param name="yaml">The filter text.</param>
    Task Save(string name, string yaml);

    /// <summary>Deletes the filter file.</summary>
    /// <param name="name">Name as <see cref="List"/> gives it.</param>
    Task Remove(string name);

    /// <summary>Moves the filter file to a new name.</summary>
    /// <param name="fromName">Current name.</param>
    /// <param name="toName">New name.</param>
    Task Rename(string fromName, string toName);
}

#if MOTELY_FILESYSTEM
/// <summary>JamlFiles over Bootsharp.FileSystem. JS calls <c>fs.init(IFileMounter)</c> from
/// <c>@rewaffle/bootsharp-file-system</c> before <c>boot()</c>. Every call that can fail runs
/// through <see cref="Guard"/>, so its reason reaches <c>Errors.last()</c>.</summary>
/// <param name="mounter">Bootsharp.FileSystem's mounter, injected by AddBootsharp.</param>
public sealed class JamlFilesModule(IFileMounter mounter) : IJamlFiles, IFileWatcher
{
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
    public Task<bool> PickFolder() => Guard.RunAsync(async () =>
    {
        string? root = await mounter.PickRoot(new PickOptions { Id = "yaml-filters", Mode = PermissionMode.ReadWrite });
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
    });

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
    public Task<string> Load(string name) =>
        Guard.RunAsync(async () => Encoding.UTF8.GetString(await Fs.ReadFile(JamlFileNames.ToUri(name))));

    /// <inheritdoc/>
    public Task Save(string name, string yaml) =>
        Guard.RunAsync(() => Fs.WriteFile(JamlFileNames.ToUri(name), Encoding.UTF8.GetBytes(yaml)));

    /// <inheritdoc/>
    public Task Remove(string name) => Guard.RunAsync(() => Fs.DeleteFile(JamlFileNames.ToUri(name)));

    /// <inheritdoc/>
    public Task Rename(string fromName, string toName) =>
        Guard.RunAsync(() => Fs.MoveFile(JamlFileNames.ToUri(fromName), JamlFileNames.ToUri(toName)));

    /// <summary>Keeps <see cref="List"/> in step with the folder and raises <see cref="OnChange"/>.</summary>
    /// <param name="changes">The folder's changes, from Bootsharp.FileSystem.</param>
    /// <returns>Done.</returns>
    public Task HandleFileChanges(IReadOnlyList<Change> changes)
    {
        foreach (var change in changes)
        {
            if (!change.File)
                continue;
            var type = change.Type switch
            {
                ChangeType.Added => JamlFileEvent.Added,
                ChangeType.Removed => JamlFileEvent.Removed,
                ChangeType.Moved => JamlFileEvent.Moved,
                _ => JamlFileEvent.Modified,
            };
            if (JamlFileNames.Change(type, change.Entry.Uri, change.Moved ? change.FromUri : null) is not { } seen)
                continue;
            if (seen.Kind == "removed")
                _names.Remove(seen.Name);
            else
            {
                if (seen.FromName is { } from)
                    _names.Remove(from);
                _names.Add(seen.Name);
            }
            OnChange?.Invoke(seen);
        }
        return Task.CompletedTask;
    }

    private IFileSystem Fs =>
        _fs ?? throw new InvalidOperationException("No folder is mounted: call JamlFiles.pickFolder() first.");
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
    public Task Save(string name, string yaml) => Task.FromException(NoFileSystem());

    /// <inheritdoc/>
    public Task Remove(string name) => Task.FromException(NoFileSystem());

    /// <inheritdoc/>
    public Task Rename(string fromName, string toName) => Task.FromException(NoFileSystem());

    /// <summary>The rejection, its message recorded for Errors.last(): a faulted task reaches JS
    /// without passing Guard.</summary>
    private static NotSupportedException NoFileSystem()
    {
        var e = new NotSupportedException(
            "This motely-wasm build has no folder access (built without Bootsharp.FileSystem).");
        Errors.Record(e);
        return e;
    }
}
#endif
