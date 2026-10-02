using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Motely.Distributed;

/// <summary>
/// The wire between MotelyHome and MotelyWorker. A filter is the unit of work and its slug is
/// its id: queue a JAML, every worker on the LAN grinds it, the finds land under that slug.
/// </summary>
/// <param name="Filter">The filter's slug.</param>
/// <param name="Start">First engine batch of the slice (at <paramref name="BatchChars"/>).</param>
/// <param name="End">One past the last batch; <c>[Start, End)</c>, like every range here.</param>
public sealed record Work(string Filter, string Jaml, int BatchChars, long Start, long End);

public sealed record FoundSeed(string Seed, int Score);

/// <summary>A finished slice. <see cref="Seeds"/> is capped (<see cref="MaxSeeds"/>); the rest of a
/// match-everything filter is not worth the bytes.</summary>
public sealed record WorkDone(
    string Filter,
    string Worker,
    long Start,
    long End,
    long SeedsSearched,
    FoundSeed[] Seeds
)
{
    public const int MaxSeeds = 10_000;
}

public sealed record FilterStatus(
    string Filter,
    string Name,
    int BatchChars,
    long BatchesDone,
    long Batches,
    int Finds,
    bool Complete,
    string[] Workers
)
{
    public double Percent => Batches == 0 ? 100 : 100.0 * BatchesDone / Batches;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Work))]
[JsonSerializable(typeof(WorkDone))]
[JsonSerializable(typeof(FilterStatus))]
[JsonSerializable(typeof(FilterStatus[]))]
public sealed partial class HomeJson : JsonSerializerContext;

/// <summary>
/// MotelyHome shouts <c>motely-home:&lt;port&gt;</c> over UDP broadcast every few seconds; a
/// worker started with no <c>--home</c> listens on <see cref="Port"/> and takes the sender's
/// address plus that port as the home URL.
/// </summary>
public static class HomeBeacon
{
    public const int Port = 35035;
    private const string Prefix = "motely-home:";

    public static byte[] Message(int httpPort) =>
        Encoding.ASCII.GetBytes(Prefix + httpPort.ToString(CultureInfo.InvariantCulture));

    public static bool TryParse(ReadOnlySpan<byte> datagram, out int httpPort)
    {
        httpPort = 0;
        Span<char> text = stackalloc char[32];
        if (datagram.Length > text.Length || Encoding.ASCII.GetChars(datagram, text) != datagram.Length)
            return false;
        var s = text[..datagram.Length];
        return s.StartsWith(Prefix)
            && int.TryParse(s[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out httpPort)
            && httpPort is > 0 and < 65536;
    }
}

public static class FilterSlug
{
    /// <summary><c>Negative Perkeo (ante 1)</c> → <c>negative-perkeo-ante-1</c>.</summary>
    public static string Of(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().TrimEnd('-');
    }
}
