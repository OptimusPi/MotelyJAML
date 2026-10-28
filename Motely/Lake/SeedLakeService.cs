using System.Collections.Concurrent;
using Motely.SeedProviders;
using Parquet;
using Parquet.Data;

namespace Motely.Lake;

public sealed class SeedLakeService : ISeedLakeService
{
    private readonly string _rootPath;

    public string RootPath => _rootPath;

    public SeedLakeService(string? rootPath = null)
    {
        _rootPath = rootPath
            ?? Environment.GetEnvironmentVariable("BALATRO_LAKE_PATH")
            ?? Environment.GetEnvironmentVariable("DUNGMOT_PATH")
            ?? (Directory.Exists(@"D:\dungmot") ? @"D:\dungmot" : AppContext.BaseDirectory);
    }

    public IReadOnlyList<LakeMetadata> GetAvailableLakes()
    {
        var list = new List<LakeMetadata>();
        if (!Directory.Exists(_rootPath))
            return list;

        // 1. Curated Ranks: ranks/*_best.txt
        string ranksDir = Path.Combine(_rootPath, "ranks");
        if (Directory.Exists(ranksDir))
        {
            foreach (var file in Directory.EnumerateFiles(ranksDir, "*_best.txt"))
            {
                string part = Path.GetFileNameWithoutExtension(file).Replace("_best", "");
                long count = new FileInfo(file).Length / 9; // ~8 chars + newline
                list.Add(new LakeMetadata(
                    Name: $"Curated Rank: {part}",
                    Path: file,
                    Category: LakeCategory.CuratedRanks,
                    Partition: part,
                    EstimatedSeedCount: count,
                    IsParquet: false
                ));
            }
        }

        // 2. Curated Suits: suits/*_best.txt
        string suitsDir = Path.Combine(_rootPath, "suits");
        if (Directory.Exists(suitsDir))
        {
            foreach (var file in Directory.EnumerateFiles(suitsDir, "*_best.txt"))
            {
                string part = Path.GetFileNameWithoutExtension(file).Replace("_best", "");
                long count = new FileInfo(file).Length / 9;
                list.Add(new LakeMetadata(
                    Name: $"Curated Suit: {part}",
                    Path: file,
                    Category: LakeCategory.CuratedSuits,
                    Partition: part,
                    EstimatedSeedCount: count,
                    IsParquet: false
                ));
            }
        }

        // 3. Negative CSVs
        string uncommonCsv = Path.Combine(_rootPath, "uncommonss.csv");
        if (File.Exists(uncommonCsv))
        {
            list.Add(new LakeMetadata(
                Name: "Negative Uncommons (Triple)",
                Path: uncommonCsv,
                Category: LakeCategory.NegativeUncommon,
                Partition: "uncommonss",
                EstimatedSeedCount: 410,
                IsParquet: false
            ));
        }

        string negraresCsv = Path.Combine(_rootPath, "negrares.csv");
        if (File.Exists(negraresCsv))
        {
            list.Add(new LakeMetadata(
                Name: "Negative Rares",
                Path: negraresCsv,
                Category: LakeCategory.NegativeRare,
                Partition: "negrares",
                EstimatedSeedCount: 0,
                IsParquet: false
            ));
        }

        string tripleNegCsv = Path.Combine(_rootPath, "triple-negative.csv");
        if (File.Exists(tripleNegCsv))
        {
            list.Add(new LakeMetadata(
                Name: "Triple Negative Jokers",
                Path: tripleNegCsv,
                Category: LakeCategory.TripleNegative,
                Partition: "triple-negative",
                EstimatedSeedCount: 0,
                IsParquet: false
            ));
        }

        // 4. Parquet ranks: parquet_lake/ranks/*.parquet
        string pqRanksDir = Path.Combine(_rootPath, "parquet_lake", "ranks");
        if (Directory.Exists(pqRanksDir))
        {
            foreach (var file in Directory.EnumerateFiles(pqRanksDir, "*.parquet"))
            {
                string part = Path.GetFileNameWithoutExtension(file);
                long fileLen = new FileInfo(file).Length;
                long estCount = part.StartsWith("little_", StringComparison.OrdinalIgnoreCase)
                    ? 200
                    : Math.Max(100_000, fileLen / 13);
                list.Add(new LakeMetadata(
                    Name: $"Parquet Rank: {part}",
                    Path: file,
                    Category: LakeCategory.Ranks,
                    Partition: part,
                    EstimatedSeedCount: estCount,
                    IsParquet: true
                ));
            }
        }

        // 5. Parquet suits: parquet_lake/suits/*.parquet
        string pqSuitsDir = Path.Combine(_rootPath, "parquet_lake", "suits");
        if (Directory.Exists(pqSuitsDir))
        {
            foreach (var file in Directory.EnumerateFiles(pqSuitsDir, "*.parquet"))
            {
                string part = Path.GetFileNameWithoutExtension(file);
                long fileLen = new FileInfo(file).Length;
                long estCount = part.StartsWith("little_", StringComparison.OrdinalIgnoreCase)
                    ? 200
                    : Math.Max(100_000, fileLen / 13);
                list.Add(new LakeMetadata(
                    Name: $"Parquet Suit: {part}",
                    Path: file,
                    Category: LakeCategory.Suits,
                    Partition: part,
                    EstimatedSeedCount: estCount,
                    IsParquet: true
                ));
            }
        }

        return list;
    }

    public IEnumerable<string> ReadSeeds(LakeCategory category, string partition, int? limit = null)
    {
        if (limit.HasValue && limit.Value <= 0)
            return [];

        string filePath = ResolveFilePath(category, partition);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Lake partition '{partition}' under category '{category}' not found at: {filePath}", filePath);

        return category switch
        {
            LakeCategory.CuratedRanks or LakeCategory.CuratedSuits => ReadTextSeeds(filePath, limit),
            LakeCategory.NegativeUncommon or LakeCategory.NegativeRare or LakeCategory.TripleNegative => ReadCsvSeeds(filePath, limit),
            LakeCategory.Ranks or LakeCategory.Suits => ReadParquetSeeds(filePath, limit),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown lake category"),
        };
    }

    public IMotelySeedProvider CreateSeedProvider(LakeCategory category, string partition, int? limit = null)
    {
        var seeds = ReadSeeds(category, partition, limit);
        return new MotelySeedListProvider(seeds, limit ?? -1);
    }

    private string ResolveFilePath(LakeCategory category, string partition)
    {
        return category switch
        {
            LakeCategory.CuratedRanks => ResolveCuratedPath("ranks", partition),
            LakeCategory.CuratedSuits => ResolveCuratedPath("suits", partition),
            LakeCategory.NegativeUncommon => Path.Combine(_rootPath, "uncommonss.csv"),
            LakeCategory.NegativeRare => Path.Combine(_rootPath, "negrares.csv"),
            LakeCategory.TripleNegative => Path.Combine(_rootPath, "triple-negative.csv"),
            LakeCategory.Ranks => ResolveParquetPath("ranks", partition),
            LakeCategory.Suits => ResolveParquetPath("suits", partition),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }

    private string ResolveCuratedPath(string subDir, string partition)
    {
        string dir = Path.Combine(_rootPath, subDir);
        string name = partition;
        if (!name.EndsWith("_best.txt", StringComparison.OrdinalIgnoreCase))
        {
            if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            name += "_best.txt";
        }
        return Path.Combine(dir, name);
    }

    private string ResolveParquetPath(string subDir, string partition)
    {
        string dir = Path.Combine(_rootPath, "parquet_lake", subDir);
        string name = partition;
        if (!name.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            name += ".parquet";
        return Path.Combine(dir, name);
    }

    private static IEnumerable<string> ReadTextSeeds(string filePath, int? limit)
    {
        int yielded = 0;
        foreach (var line in File.ReadLines(filePath))
        {
            string seed = line.Trim();
            if (seed.Length > 0 && seed[0] != '#')
            {
                yield return seed;
                yielded++;
                if (limit.HasValue && yielded >= limit.Value)
                    yield break;
            }
        }
    }

    private static IEnumerable<string> ReadCsvSeeds(string filePath, int? limit)
    {
        int yielded = 0;
        foreach (var line in File.ReadLines(filePath))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("Progress", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("SEED", StringComparison.OrdinalIgnoreCase))
                continue;

            int comma = trimmed.IndexOf(',');
            string seed = comma >= 0 ? trimmed[..comma].Trim() : trimmed;
            if (seed.Length > 0)
            {
                yield return seed;
                yielded++;
                if (limit.HasValue && yielded >= limit.Value)
                    yield break;
            }
        }
    }

    private static IEnumerable<string> ReadParquetSeeds(string filePath, int? limit)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();

        var fields = reader.Schema.GetDataFields();
        if (fields.Length == 0)
            yield break;

        var seedField = fields.FirstOrDefault(f => string.Equals(f.Name, "seed", StringComparison.OrdinalIgnoreCase))
            ?? fields[0];

        int yielded = 0;
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var groupReader = reader.OpenRowGroupReader(i);
            var col = groupReader.ReadColumnAsync(seedField).GetAwaiter().GetResult();
            if (col?.Data is string[] strArray)
            {
                foreach (var s in strArray)
                {
                    if (!string.IsNullOrEmpty(s))
                    {
                        yield return s;
                        yielded++;
                        if (limit.HasValue && yielded >= limit.Value)
                            yield break;
                    }
                }
            }
            else if (col?.Data is Array arr)
            {
                foreach (var item in arr)
                {
                    if (item != null)
                    {
                        string? s = item.ToString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            yield return s;
                            yielded++;
                            if (limit.HasValue && yielded >= limit.Value)
                                yield break;
                        }
                    }
                }
            }
        }
    }
}
