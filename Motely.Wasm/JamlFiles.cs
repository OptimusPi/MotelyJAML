using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootsharp.FileSystem;

/// <summary>
/// A filter file event under the mounted folder. <c>Kind</c> is one of
/// <c>added</c> / <c>removed</c> / <c>modified</c> / <c>moved</c>; <c>Name</c> is what
/// <see cref="JamlFiles.Load"/> and friends take: <c>.jaml</c> files drop the extension
/// (<c>sub/filter</c>), <c>.yaml</c> / <c>.yml</c> / <c>.json</c> keep it (<c>sub/filter.json</c>).
/// </summary>
public readonly record struct JamlFileChange(string Kind, string Name, string? FromName);

/// <summary>
/// Local .jaml files through the browser's File System Access API (Bootsharp.FileSystem).
/// <c>JamlFiles.pickFolder()</c> once, then <c>list()</c> / <c>load(name)</c> / <c>save(name, jaml)</c> /
/// <c>delete(name)</c>. <see cref="OnChange"/> fires for every .jaml add/remove/modify/move under the
/// folder - including the initial listing right after mount - so a UI can just subscribe and
/// re-render. Everything else in the engine works without a mounted folder; this is the only
/// door that needs one.
///
/// JS side: <c>@rewaffle/bootsharp-file-system</c> (sponsor registry) must be initialized before
/// <c>bootsharp.boot()</c>: <c>fs.init(Bootsharp.FileSystem.FileMounter)</c>. Consumers without
/// the package can still boot; only this class is dead.
/// </summary>
public static partial class JamlFiles
{
    /// <summary>Default extension. A name with no known extension means <c>name.jaml</c>.</summary>
    private const string Ext = ".jaml";

    /// <summary>Every extension the loader reads. JSON is YAML, so all four go through
    /// <see cref="Motely.Filters.Jaml.JamlConfigLoader.FromJaml"/> unchanged.</summary>
    private static readonly string[] Exts = [".jaml", ".yaml", ".yml", ".json"];

    private static string? _rootId;
    private static IFileSystem? _fs;
    private static readonly SortedSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    [Export]
    public static event Action<JamlFileChange>? OnChange;

    /// <summary>True once <see cref="PickFolder"/> succeeded and until <see cref="Unmount"/>.</summary>
    [Export]
    public static bool IsMounted() => _fs is not null;

    /// <summary>
    /// Prompts for a local folder and mounts it read-write. Returns false when the user cancels.
    /// The folder's existing .jaml files arrive on <see cref="OnChange"/> as <c>added</c> and show up in
    /// <see cref="List"/> once this resolves.
    /// </summary>
    [Export]
    public static async Task<bool> PickFolder()
    {
        var mounter = MotelyServices.Get<IFileMounter>();
        string? root = await mounter.PickRoot(
            new PickOptions { Id = "jaml-filters", Mode = PermissionMode.ReadWrite }
        );
        if (root is null)
            return false;

        if (_rootId is not null)
            await Unmount();

        _names.Clear();
        _fs = await mounter.Mount(
            root,
            new Watcher(),
            new MountOptions { Mode = PermissionMode.ReadWrite, Ignore = [".git", "node_modules"] }
        );
        _rootId = root;
        return true;
    }

    [Export]
    public static async Task Unmount()
    {
        if (_rootId is null)
            return;
        string root = _rootId;
        _rootId = null;
        _fs = null;
        _names.Clear();
        await MotelyServices.Get<IFileMounter>().Unmount(root);
    }

    /// <summary>Every filter file under the folder, sorted, recursive. Nested files keep their
    /// relative path: <c>sub/filter</c> (a .jaml), <c>sub/other.yaml</c>, <c>sub/x.json</c>.</summary>
    [Export]
    public static string[] List() => [.. _names];

    [Export]
    public static async Task<string> Load(string name) =>
        Encoding.UTF8.GetString(await Fs.ReadFile(ToUri(name)));

    [Export]
    public static Task Save(string name, string jaml) =>
        Fs.WriteFile(ToUri(name), Encoding.UTF8.GetBytes(jaml));

    [Export]
    public static Task Delete(string name) => Fs.DeleteFile(ToUri(name));

    [Export]
    public static Task Rename(string fromName, string toName) =>
        Fs.MoveFile(ToUri(fromName), ToUri(toName));

    private static IFileSystem Fs =>
        _fs ?? throw new InvalidOperationException("No folder mounted - call JamlFiles.pickFolder() first.");

    private static bool HasFilterExt(string path) =>
        Exts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>"filter" -> "/filter.jaml"; "sub/x.json" -> "/sub/x.json"; "/a/b.yml" -> "/a/b.yml".</summary>
    private static string ToUri(string name)
    {
        string n = name.Replace('\\', '/').TrimStart('/');
        if (!HasFilterExt(n))
            n += Ext;
        return "/" + n;
    }

    /// <summary>"/sub/filter.jaml" -> "sub/filter"; "/sub/x.yaml" -> "sub/x.yaml";
    /// null when the file is not a filter.</summary>
    private static string? ToName(string uri)
    {
        string n = uri.TrimStart('/');
        if (n.EndsWith(Ext, StringComparison.OrdinalIgnoreCase))
            return n[..^Ext.Length];
        return HasFilterExt(n) ? n : null;
    }

    private sealed class Watcher : IFileWatcher
    {
        public Task HandleFileChanges(IReadOnlyList<Change> changes)
        {
            foreach (var c in changes)
            {
                if (!c.File)
                    continue;
                string? name = ToName(c.Entry.Uri);
                string? from = c.Moved ? ToName(c.FromUri) : null;
                if (name is null && from is null)
                    continue;

                switch (c.Type)
                {
                    case ChangeType.Added:
                        _names.Add(name!);
                        break;
                    case ChangeType.Removed:
                        _names.Remove(name!);
                        break;
                    case ChangeType.Moved:
                        if (from is not null) _names.Remove(from);
                        if (name is not null) _names.Add(name);
                        break;
                }

                string kind = c.Type switch
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
    }
}
