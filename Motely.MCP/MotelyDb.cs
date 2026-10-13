using System.Reflection;
using System.Text;
using System.Text.Json;
using Motely.Filters;
using Npgsql;
using NpgsqlTypes;

namespace Motely.MCP;

/// <summary>
/// The seed database: Neon in production, Aspire's Postgres container locally, any Postgres in
/// between. The connection string arrives as <c>ConnectionStrings__db</c> (Aspire's shape) or
/// <c>MOTELY_DB</c>, in Npgsql's key=value form or as the <c>postgresql://</c> URL Neon and Vercel
/// hand out. Schema changes are the embedded <c>Migrations/*.sql</c> files, applied in name order,
/// once each. Every query here is plain SQL with positional parameters: no ORM, no reflection.
/// </summary>
internal sealed class MotelyDb : IAsyncDisposable
{
    private readonly NpgsqlDataSource _source;

    private MotelyDb(NpgsqlDataSource source) => _source = source;

    public static MotelyDb? FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("ConnectionStrings__db");
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable("MOTELY_DB");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var builder = new NpgsqlDataSourceBuilder(ToConnectionString(raw));
        builder.ConnectionStringBuilder.ApplicationName ??= "motely-mcp";
        return new(builder.Build());
    }

    public ValueTask DisposeAsync() => _source.DisposeAsync();

    /// <summary>
    /// Npgsql reads key=value strings; Neon's dashboard and the Vercel integration give
    /// <c>postgresql://user:password@host/db?sslmode=require&amp;channel_binding=require</c>.
    /// Anything that isn't a URL goes through untouched.
    /// </summary>
    internal static string ToConnectionString(string raw)
    {
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return raw;

        // ArgumentException is what the startup guard reports as "unreachable"; a bare
        // UriFormatException would escape it as a stack trace. The message leaves the URL out:
        // it carries the password.
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            throw new ArgumentException(
                "the postgresql:// connection string is not a valid URL (percent-encode @, /, # and : in the password)."
            );
        var builder = new NpgsqlConnectionStringBuilder { Host = uri.Host, Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')) };
        if (uri.Port > 0)
            builder.Port = uri.Port;
        var user = uri.UserInfo.Split(':', 2);
        if (user[0].Length > 0)
            builder.Username = Uri.UnescapeDataString(user[0]);
        if (user.Length > 1)
            builder.Password = Uri.UnescapeDataString(user[1]);

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            switch (Uri.UnescapeDataString(parts[0]).ToLowerInvariant())
            {
                case "sslmode":
                    builder.SslMode = value.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => SslMode.Require,
                    };
                    break;
                case "channel_binding":
                    builder.ChannelBinding = value.ToLowerInvariant() switch
                    {
                        "disable" => ChannelBinding.Disable,
                        "prefer" => ChannelBinding.Prefer,
                        _ => ChannelBinding.Require,
                    };
                    break;
                case "application_name":
                    builder.ApplicationName = value;
                    break;
                case "options":
                    builder.Options = value;
                    break;
                case "connect_timeout" when int.TryParse(value, out var seconds):
                    builder.Timeout = seconds;
                    break;
            }
        }
        return builder.ConnectionString;
    }

    /// <summary>
    /// The filter's name in the database: its <c>id:</c>, else its name, else the file it came from,
    /// as lower-case words joined by dashes. Inline text with no name is <c>unnamed</c>.
    /// </summary>
    public static string Slug(JamlConfig config, string source)
    {
        var basis =
            config.Id is { Length: > 0 } id ? id
            : config.Name is { Length: > 0 } name ? name
            : source == "jaml" ? "unnamed"
            : Path.GetFileNameWithoutExtension(source);
        var slug = new StringBuilder(basis.Length);
        foreach (var c in basis.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        var text = slug.ToString().TrimEnd('-');
        return text.Length == 0 ? "unnamed" : text;
    }

    /// <summary>Applies every embedded migration not yet in <c>schema_migrations</c>. Returns how many ran.</summary>
    public async Task<int> MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);
        await using (var create = new NpgsqlCommand(
            "create table if not exists schema_migrations (name text primary key, applied_at timestamptz not null default now())",
            connection))
            await create.ExecuteNonQueryAsync(cancellationToken);

        int applied = 0;
        foreach (var (name, sql) in Migrations())
        {
            // One transaction per script, under a lock so two servers starting together don't race.
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var hold = new NpgsqlCommand("select pg_advisory_xact_lock(7205759403792793)", connection, transaction))
                await hold.ExecuteNonQueryAsync(cancellationToken);
            await using (var done = new NpgsqlCommand("select 1 from schema_migrations where name = $1", connection, transaction))
            {
                done.Parameters.Add(new NpgsqlParameter { Value = name, NpgsqlDbType = NpgsqlDbType.Text });
                if (await done.ExecuteScalarAsync(cancellationToken) is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }
            }
            await using (var run = new NpgsqlCommand(sql, connection, transaction))
                await run.ExecuteNonQueryAsync(cancellationToken);
            await using (var record = new NpgsqlCommand("insert into schema_migrations (name) values ($1)", connection, transaction))
            {
                record.Parameters.Add(new NpgsqlParameter { Value = name, NpgsqlDbType = NpgsqlDbType.Text });
                await record.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            applied++;
        }
        return applied;
    }

    private static IEnumerable<(string Name, string Sql)> Migrations()
    {
        var assembly = typeof(MotelyDb).Assembly;
        const string prefix = "Migrations.";
        foreach (var resource in assembly.GetManifestResourceNames().Where(static n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            yield return (resource[prefix.Length..], reader.ReadToEnd());
        }
    }

    /// <summary>
    /// Records the filter (its text as last run) and upserts each seed under it. A seed found again
    /// takes the newer score and tallies. Tallies become <c>{"label": count, ...}</c> in column order.
    /// </summary>
    public async Task SaveFindsAsync(
        string slug,
        string? name,
        string jaml,
        IReadOnlyList<string> tallyLabels,
        IEnumerable<(string Seed, int Score, int[] Tallies)> finds,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var batch = new NpgsqlBatch(connection, transaction);

        var filter = new NpgsqlBatchCommand(
            """
            insert into filters (slug, name, jaml) values ($1, $2, $3)
            on conflict (slug) do update set name = excluded.name, jaml = excluded.jaml, updated_at = now()
            """
        );
        filter.Parameters.Add(new NpgsqlParameter { Value = slug, NpgsqlDbType = NpgsqlDbType.Text });
        filter.Parameters.Add(new NpgsqlParameter { Value = (object?)name ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
        filter.Parameters.Add(new NpgsqlParameter { Value = jaml, NpgsqlDbType = NpgsqlDbType.Text });
        batch.BatchCommands.Add(filter);

        foreach (var (seed, score, tallies) in finds)
        {
            var row = new NpgsqlBatchCommand(
                """
                insert into seeds (seed, filter_slug, score, tallies, found_by) values ($1, $2, $3, $4, $5)
                on conflict (seed, filter_slug) do update
                    set score = excluded.score, tallies = excluded.tallies, found_by = excluded.found_by, found_at = now()
                """
            );
            row.Parameters.Add(new NpgsqlParameter { Value = seed, NpgsqlDbType = NpgsqlDbType.Text });
            row.Parameters.Add(new NpgsqlParameter { Value = slug, NpgsqlDbType = NpgsqlDbType.Text });
            row.Parameters.Add(new NpgsqlParameter { Value = score, NpgsqlDbType = NpgsqlDbType.Integer });
            row.Parameters.Add(new NpgsqlParameter { Value = TalliesJson(tallyLabels, tallies), NpgsqlDbType = NpgsqlDbType.Jsonb });
            row.Parameters.Add(new NpgsqlParameter { Value = Environment.MachineName, NpgsqlDbType = NpgsqlDbType.Text });
            batch.BatchCommands.Add(row);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string TalliesJson(IReadOnlyList<string> labels, int[] tallies)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            for (int i = 0; i < tallies.Length; i++)
                json.WriteNumber(i < labels.Count ? labels[i] : $"score{i}", tallies[i]);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    public readonly record struct SavedSeed(string Seed, int Score, string Tallies, DateTime FoundAt);

    /// <summary>A filter's seeds, best first; ties in the order they were found.</summary>
    public async Task<List<SavedSeed>> BestSeedsAsync(string slug, int top, CancellationToken cancellationToken)
    {
        await using var command = _source.CreateCommand(
            "select seed, score, tallies::text, found_at from seeds where filter_slug = $1 order by score desc, found_at limit $2"
        );
        command.Parameters.Add(new NpgsqlParameter { Value = slug, NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = top, NpgsqlDbType = NpgsqlDbType.Integer });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<SavedSeed> rows = [];
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetDateTime(3)));
        return rows;
    }

    public readonly record struct SavedFilter(string Slug, string? Name, long Seeds, DateTime UpdatedAt);

    /// <summary>Every filter with its seed count, most recently run first.</summary>
    public async Task<List<SavedFilter>> FiltersAsync(CancellationToken cancellationToken)
    {
        await using var command = _source.CreateCommand(
            """
            select f.slug, f.name, count(s.seed), f.updated_at
            from filters f left join seeds s on s.filter_slug = f.slug
            group by f.slug order by f.updated_at desc
            """
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<SavedFilter> rows = [];
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2), reader.GetDateTime(3)));
        return rows;
    }
}
