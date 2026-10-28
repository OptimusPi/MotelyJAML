using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Motely.Filters;

namespace Motely.MCP.Data;

/// <summary>
/// Zero-config, thread-safe atomic JSON file-backed implementation of <see cref="IMotelyStore"/>.
/// Persists filters, discovered seeds, leaderboards, and Community Search Party state across restarts.
/// </summary>
public sealed class FileMotelyStore : IMotelyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _dataDir;
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StoreSnapshot _state = new();
    private bool _initialized;

    public string DataDirectory => _dataDir;

    public FileMotelyStore(string? dataDirectory = null)
    {
        _dataDir =
            !string.IsNullOrWhiteSpace(dataDirectory)
                ? Path.GetFullPath(dataDirectory)
                : Path.Combine(AppContext.BaseDirectory, "motely-data");
        _stateFilePath = Path.Combine(_dataDir, "motely-store.json");
    }

    public async Task InitializeAsync(string? filtersDirectory = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            Directory.CreateDirectory(_dataDir);
            if (File.Exists(_stateFilePath))
            {
                try
                {
                    await using var stream = File.OpenRead(_stateFilePath);
                    var loaded = await JsonSerializer
                        .DeserializeAsync<StoreSnapshot>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    if (loaded is not null)
                        _state = loaded;
                }
                catch
                {
                    // Corrupted or empty snapshot; start fresh.
                    _state = new StoreSnapshot();
                }
            }

            bool dirty = SeedFiltersFromDirectory(filtersDirectory);
            if (dirty || !File.Exists(_stateFilePath))
            {
                await SaveUnlockedAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SavedFilterRecord>> GetFiltersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return
            [
                .. _state
                    .Filters.Values.OrderBy(f => f.Slug, StringComparer.OrdinalIgnoreCase),
            ];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SavedFilterRecord?> GetFilterAsync(string slug, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _state.Filters.TryGetValue(NormalizeSlug(slug), out var found) ? found : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SavedFilterRecord> SaveFilterAsync(
        SavedFilterRecord filter,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string key = NormalizeSlug(filter.Slug);
            var now = DateTimeOffset.UtcNow;
            var created = _state.Filters.TryGetValue(key, out var existing)
                ? existing.CreatedAtUtc
                : (filter.CreatedAtUtc == default ? now : filter.CreatedAtUtc);

            var updated = filter with
            {
                Slug = filter.Slug.Trim(),
                CreatedAtUtc = created,
                UpdatedAtUtc = now,
            };
            _state.Filters[key] = updated;
            await SaveUnlockedAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RecordSeedsAsync(
        IEnumerable<DiscoveredSeedRecord> seeds,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int recorded = 0;
            foreach (var item in seeds)
            {
                if (string.IsNullOrWhiteSpace(item.Seed))
                    continue;

                string normSeed = item.Seed.Trim().ToUpperInvariant();
                string normFilter = NormalizeSlug(item.FilterSlug);
                string compositeKey = $"{normFilter}::{normSeed}";

                if (_state.Seeds.TryGetValue(compositeKey, out var existing))
                {
                    if (item.Score > existing.Score)
                    {
                        _state.Seeds[compositeKey] = item with
                        {
                            Seed = normSeed,
                            FoundAtUtc = item.FoundAtUtc == default ? DateTimeOffset.UtcNow : item.FoundAtUtc,
                        };
                        recorded++;
                    }
                }
                else
                {
                    _state.Seeds[compositeKey] = item with
                    {
                        Seed = normSeed,
                        FoundAtUtc = item.FoundAtUtc == default ? DateTimeOffset.UtcNow : item.FoundAtUtc,
                    };
                    recorded++;
                }
            }

            if (recorded > 0)
            {
                await SaveUnlockedAsync(cancellationToken).ConfigureAwait(false);
            }

            return recorded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DiscoveredSeedRecord>> GetRecentFindsAsync(
        string? filterSlug = null,
        int limit = 50,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IEnumerable<DiscoveredSeedRecord> query = _state.Seeds.Values;
            if (!string.IsNullOrWhiteSpace(filterSlug))
            {
                string norm = NormalizeSlug(filterSlug);
                query = query.Where(s => NormalizeSlug(s.FilterSlug) == norm);
            }

            return
            [
                .. query
                    .OrderByDescending(s => s.FoundAtUtc)
                    .ThenByDescending(s => s.Score)
                    .ThenBy(s => s.Seed, StringComparer.Ordinal)
                    .Take(Math.Clamp(limit, 1, 500)),
            ];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DiscoveredSeedRecord>> GetLeaderboardAsync(
        string? filterSlug = null,
        string? deck = null,
        string? stake = null,
        int limit = 50,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IEnumerable<DiscoveredSeedRecord> query = _state.Seeds.Values;
            if (!string.IsNullOrWhiteSpace(filterSlug))
            {
                string norm = NormalizeSlug(filterSlug);
                query = query.Where(s => NormalizeSlug(s.FilterSlug) == norm);
            }
            if (!string.IsNullOrWhiteSpace(deck))
            {
                query = query.Where(s => string.Equals(s.Deck, deck.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(stake))
            {
                query = query.Where(s => string.Equals(s.Stake, stake.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            return
            [
                .. query
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.Seed, StringComparer.Ordinal)
                    .Take(Math.Clamp(limit, 1, 500)),
            ];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PartyRecord>> GetPartiesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return
            [
                .. _state
                    .Parties.Values.OrderByDescending(p => p.CreatedAtUtc)
                    .ThenBy(p => p.PartyId, StringComparer.Ordinal),
            ];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PartyRecord?> GetPartyAsync(string partyId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _state.Parties.TryGetValue(partyId, out var party) ? party : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PartyRecord> SavePartyAsync(PartyRecord party, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var updated = party with { UpdatedAtUtc = DateTimeOffset.UtcNow };
            _state.Parties[party.PartyId] = updated;
            await SaveUnlockedAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
            await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
    }

    internal bool SeedFiltersFromDirectory(string? filtersDirectory)
    {
        string? dir = filtersDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return false;

        bool changed = false;
        var files = Directory
            .EnumerateFiles(dir, "*.yaml")
            .Concat(Directory.EnumerateFiles(dir, "*.jaml"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            string slug = Path.GetFileNameWithoutExtension(file);
            string key = NormalizeSlug(slug);
            if (_state.Filters.ContainsKey(key))
                continue;

            try
            {
                string text = File.ReadAllText(file);
                if (!JamlConfigLoader.TryLoad(text, out var cfg, out _))
                    continue;

                var now = DateTimeOffset.UtcNow;
                _state.Filters[key] = new SavedFilterRecord(
                    Slug: slug,
                    Name: string.IsNullOrWhiteSpace(cfg.Name) ? slug : cfg.Name,
                    Author: cfg.Author,
                    Description: cfg.Description,
                    Deck: cfg.Deck.ToString(),
                    Stake: cfg.Stake.ToString(),
                    JamlContent: text,
                    CreatedAtUtc: now,
                    UpdatedAtUtc: now
                );
                changed = true;
            }
            catch
            {
                // Ignore unreadable files during initial seeding
            }
        }

        return changed;
    }

    private async Task SaveUnlockedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDir);
        string tempPath = $"{_stateFilePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, _state, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(tempPath, _stateFilePath, overwrite: true);
    }

    internal static string NormalizeSlug(string slug) =>
        (slug ?? "").Trim().ToLowerInvariant();

    private sealed class StoreSnapshot
    {
        public Dictionary<string, SavedFilterRecord> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DiscoveredSeedRecord> Seeds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PartyRecord> Parties { get; set; } = new(StringComparer.Ordinal);
    }
}
