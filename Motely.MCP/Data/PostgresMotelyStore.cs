using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Motely.Filters;
using Npgsql;

namespace Motely.MCP.Data;

/// <summary>
/// PostgreSQL implementation of <see cref="IMotelyStore"/> using <see cref="NpgsqlDataSource"/>.
/// Automatically creates tables on startup and falls back cleanly if the database is unreachable.
/// </summary>
public sealed class PostgresMotelyStore : IMotelyStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly FileMotelyStore _fallbackStore;
    private bool _initialized;
    private bool _useFallback;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    public PostgresMotelyStore(string connectionString, string? fallbackDataDir = null)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _fallbackStore = new FileMotelyStore(fallbackDataDir);
    }

    public async Task InitializeAsync(string? filtersDirectory = null, CancellationToken cancellationToken = default)
    {
        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS motely_filters (
                        slug TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        author TEXT NULL,
                        description TEXT NULL,
                        deck TEXT NOT NULL,
                        stake TEXT NOT NULL,
                        jaml_content TEXT NOT NULL,
                        created_at TIMESTAMPTZ NOT NULL,
                        updated_at TIMESTAMPTZ NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS motely_seeds (
                        seed TEXT NOT NULL,
                        filter_slug TEXT NOT NULL,
                        filter_name TEXT NOT NULL,
                        score INT NOT NULL,
                        tallies_json TEXT NOT NULL,
                        deck TEXT NOT NULL,
                        stake TEXT NOT NULL,
                        party_id TEXT NULL,
                        worker_id TEXT NULL,
                        found_at TIMESTAMPTZ NOT NULL,
                        PRIMARY KEY (filter_slug, seed)
                    );

                    CREATE TABLE IF NOT EXISTS motely_parties (
                        party_id TEXT PRIMARY KEY,
                        payload_json TEXT NOT NULL,
                        created_at TIMESTAMPTZ NOT NULL,
                        updated_at TIMESTAMPTZ NOT NULL
                    );
                    """;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(filtersDirectory) && Directory.Exists(filtersDirectory))
                {
                    var files = Directory
                        .EnumerateFiles(filtersDirectory, "*.yaml")
                        .Concat(Directory.EnumerateFiles(filtersDirectory, "*.jaml"))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

                    foreach (string file in files)
                    {
                        string slug = Path.GetFileNameWithoutExtension(file);
                        string text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                        if (!JamlConfigLoader.TryLoad(text, out var cfg, out _))
                            continue;

                        var now = DateTimeOffset.UtcNow;
                        await UpsertFilterInternalAsync(
                            conn,
                            new SavedFilterRecord(
                                slug,
                                string.IsNullOrWhiteSpace(cfg.Name) ? slug : cfg.Name,
                                cfg.Author,
                                cfg.Description,
                                cfg.Deck.ToString(),
                                cfg.Stake.ToString(),
                                text,
                                now,
                                now
                            ),
                            insertOnly: true,
                            cancellationToken
                        ).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                _useFallback = true;
                await _fallbackStore.InitializeAsync(filtersDirectory, cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<IReadOnlyList<SavedFilterRecord>> GetFiltersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.GetFiltersAsync(cancellationToken).ConfigureAwait(false);

        var list = new List<SavedFilterRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT slug, name, author, description, deck, stake, jaml_content, created_at, updated_at
            FROM motely_filters
            ORDER BY slug ASC;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadFilter(reader));
        }
        return list;
    }

    public async Task<SavedFilterRecord?> GetFilterAsync(string slug, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.GetFilterAsync(slug, cancellationToken).ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT slug, name, author, description, deck, stake, jaml_content, created_at, updated_at
            FROM motely_filters
            WHERE LOWER(slug) = LOWER($1)
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue(slug.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadFilter(reader) : null;
    }

    public async Task<SavedFilterRecord> SaveFilterAsync(
        SavedFilterRecord filter,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.SaveFilterAsync(filter, cancellationToken).ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await UpsertFilterInternalAsync(conn, filter, insertOnly: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RecordSeedsAsync(
        IEnumerable<DiscoveredSeedRecord> seeds,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.RecordSeedsAsync(seeds, cancellationToken).ConfigureAwait(false);

        int count = 0;
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed.Seed))
                continue;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO motely_seeds (seed, filter_slug, filter_name, score, tallies_json, deck, stake, party_id, worker_id, found_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
                ON CONFLICT (filter_slug, seed) DO UPDATE
                SET score = EXCLUDED.score,
                    tallies_json = EXCLUDED.tallies_json,
                    found_at = EXCLUDED.found_at,
                    party_id = COALESCE(EXCLUDED.party_id, motely_seeds.party_id),
                    worker_id = COALESCE(EXCLUDED.worker_id, motely_seeds.worker_id)
                WHERE EXCLUDED.score > motely_seeds.score;
                """;
            cmd.Parameters.AddWithValue(seed.Seed.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue(seed.FilterSlug.Trim());
            cmd.Parameters.AddWithValue(seed.FilterName);
            cmd.Parameters.AddWithValue(seed.Score);
            cmd.Parameters.AddWithValue(JsonSerializer.Serialize(seed.Tallies ?? [], JsonOptions));
            cmd.Parameters.AddWithValue(seed.Deck);
            cmd.Parameters.AddWithValue(seed.Stake);
            cmd.Parameters.AddWithValue((object?)seed.PartyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)seed.WorkerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue(seed.FoundAtUtc == default ? DateTimeOffset.UtcNow : seed.FoundAtUtc);

            count += await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return count;
    }

    public async Task<IReadOnlyList<DiscoveredSeedRecord>> GetRecentFindsAsync(
        string? filterSlug = null,
        int limit = 50,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.GetRecentFindsAsync(filterSlug, limit, cancellationToken).ConfigureAwait(false);

        var list = new List<DiscoveredSeedRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seed, filter_slug, filter_name, score, tallies_json, deck, stake, party_id, worker_id, found_at
            FROM motely_seeds
            WHERE ($1 = '' OR LOWER(filter_slug) = LOWER($1))
            ORDER BY found_at DESC, score DESC, seed ASC
            LIMIT $2;
            """;
        cmd.Parameters.AddWithValue(filterSlug?.Trim() ?? "");
        cmd.Parameters.AddWithValue(Math.Clamp(limit, 1, 500));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadSeed(reader));
        }
        return list;
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
        if (_useFallback)
            return await _fallbackStore.GetLeaderboardAsync(filterSlug, deck, stake, limit, cancellationToken).ConfigureAwait(false);

        var list = new List<DiscoveredSeedRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seed, filter_slug, filter_name, score, tallies_json, deck, stake, party_id, worker_id, found_at
            FROM motely_seeds
            WHERE ($1 = '' OR LOWER(filter_slug) = LOWER($1))
              AND ($2 = '' OR LOWER(deck) = LOWER($2))
              AND ($3 = '' OR LOWER(stake) = LOWER($3))
            ORDER BY score DESC, seed ASC
            LIMIT $4;
            """;
        cmd.Parameters.AddWithValue(filterSlug?.Trim() ?? "");
        cmd.Parameters.AddWithValue(deck?.Trim() ?? "");
        cmd.Parameters.AddWithValue(stake?.Trim() ?? "");
        cmd.Parameters.AddWithValue(Math.Clamp(limit, 1, 500));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadSeed(reader));
        }
        return list;
    }

    public async Task<IReadOnlyList<PartyRecord>> GetPartiesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.GetPartiesAsync(cancellationToken).ConfigureAwait(false);

        var list = new List<PartyRecord>();
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM motely_parties ORDER BY created_at DESC;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var p = JsonSerializer.Deserialize<PartyRecord>(reader.GetString(0), JsonOptions);
            if (p is not null)
                list.Add(p);
        }
        return list;
    }

    public async Task<PartyRecord?> GetPartyAsync(string partyId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.GetPartyAsync(partyId, cancellationToken).ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM motely_parties WHERE party_id = $1 LIMIT 1;";
        cmd.Parameters.AddWithValue(partyId);
        var json = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is not null ? JsonSerializer.Deserialize<PartyRecord>(json, JsonOptions) : null;
    }

    public async Task<PartyRecord> SavePartyAsync(PartyRecord party, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_useFallback)
            return await _fallbackStore.SavePartyAsync(party, cancellationToken).ConfigureAwait(false);

        var updated = party with { UpdatedAtUtc = DateTimeOffset.UtcNow };
        string json = JsonSerializer.Serialize(updated, JsonOptions);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO motely_parties (party_id, payload_json, created_at, updated_at)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (party_id) DO UPDATE
            SET payload_json = EXCLUDED.payload_json,
                updated_at = EXCLUDED.updated_at;
            """;
        cmd.Parameters.AddWithValue(updated.PartyId);
        cmd.Parameters.AddWithValue(json);
        cmd.Parameters.AddWithValue(updated.CreatedAtUtc);
        cmd.Parameters.AddWithValue(updated.UpdatedAtUtc);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
            await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SavedFilterRecord> UpsertFilterInternalAsync(
        NpgsqlConnection conn,
        SavedFilterRecord filter,
        bool insertOnly,
        CancellationToken cancellationToken
    )
    {
        var now = DateTimeOffset.UtcNow;
        var updated = filter with
        {
            Slug = filter.Slug.Trim(),
            CreatedAtUtc = filter.CreatedAtUtc == default ? now : filter.CreatedAtUtc,
            UpdatedAtUtc = now,
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = insertOnly
            ? """
              INSERT INTO motely_filters (slug, name, author, description, deck, stake, jaml_content, created_at, updated_at)
              VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
              ON CONFLICT (slug) DO NOTHING;
              """
            : """
              INSERT INTO motely_filters (slug, name, author, description, deck, stake, jaml_content, created_at, updated_at)
              VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
              ON CONFLICT (slug) DO UPDATE
              SET name = EXCLUDED.name,
                  author = EXCLUDED.author,
                  description = EXCLUDED.description,
                  deck = EXCLUDED.deck,
                  stake = EXCLUDED.stake,
                  jaml_content = EXCLUDED.jaml_content,
                  updated_at = EXCLUDED.updated_at;
              """;
        cmd.Parameters.AddWithValue(updated.Slug);
        cmd.Parameters.AddWithValue(updated.Name);
        cmd.Parameters.AddWithValue((object?)updated.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)updated.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue(updated.Deck);
        cmd.Parameters.AddWithValue(updated.Stake);
        cmd.Parameters.AddWithValue(updated.JamlContent);
        cmd.Parameters.AddWithValue(updated.CreatedAtUtc);
        cmd.Parameters.AddWithValue(updated.UpdatedAtUtc);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private static SavedFilterRecord ReadFilter(NpgsqlDataReader reader) =>
        new(
            Slug: reader.GetString(0),
            Name: reader.GetString(1),
            Author: reader.IsDBNull(2) ? null : reader.GetString(2),
            Description: reader.IsDBNull(3) ? null : reader.GetString(3),
            Deck: reader.GetString(4),
            Stake: reader.GetString(5),
            JamlContent: reader.GetString(6),
            CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(7),
            UpdatedAtUtc: reader.GetFieldValue<DateTimeOffset>(8)
        );

    private static DiscoveredSeedRecord ReadSeed(NpgsqlDataReader reader) =>
        new(
            Seed: reader.GetString(0),
            FilterSlug: reader.GetString(1),
            FilterName: reader.GetString(2),
            Score: reader.GetInt32(3),
            Tallies: JsonSerializer.Deserialize<int[]>(reader.GetString(4), JsonOptions) ?? [],
            Deck: reader.GetString(5),
            Stake: reader.GetString(6),
            PartyId: reader.IsDBNull(7) ? null : reader.GetString(7),
            WorkerId: reader.IsDBNull(8) ? null : reader.GetString(8),
            FoundAtUtc: reader.GetFieldValue<DateTimeOffset>(9)
        );

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}
