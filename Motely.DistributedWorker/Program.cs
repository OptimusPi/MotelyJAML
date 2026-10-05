using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Motely.Distributed;
using Motely.DistributedWorker;

// MotelyWorker [--home http://host:35036] [--threads N] [--name NAME]
//
// No arguments: find MotelyHome on the LAN and grind whatever it has queued, forever.
string? homeUrl = null;
string name = Environment.MachineName;
int threads = Environment.ProcessorCount;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--home" when i + 1 < args.Length:
            homeUrl = args[++i];
            break;
        case "--threads" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0:
            threads = n;
            i++;
            break;
        case "--name" when i + 1 < args.Length:
            name = args[++i];
            break;
        default:
            Console.Error.WriteLine("Usage: MotelyWorker [--home http://host:35036] [--threads N] [--name NAME]");
            Console.Error.WriteLine("With no --home, the worker listens for MotelyHome's LAN beacon.");
            return args[i] is "-h" or "--help" ? 0 : 1;
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
bool discover = homeUrl is null;

while (!cts.Token.IsCancellationRequested)
{
    if (homeUrl is null)
    {
        Log($"Listening for MotelyHome on the LAN (UDP {HomeBeacon.Port})...");
        try
        {
            homeUrl = await FindHomeAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    var worker = new HomeWorker(new HttpHome(http, homeUrl), name)
    {
        Threads = threads,
        Log = Log,
        // A found home that goes quiet for a couple of minutes may have moved: listen again.
        MaxConsecutiveFailures = discover ? 8 : int.MaxValue,
    };
    Log($"MotelyWorker {name} -> {homeUrl} | threads={threads}");
    bool cancelled = await worker.RunAsync(cts.Token);
    Log($"{worker.Claims} claims, {worker.SeedsSearched:N0} seeds, {worker.Finds} finds.");
    if (cancelled)
        break;
    homeUrl = null;
}
return 0;

static void Log(string line) =>
    Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");

static async Task<string> FindHomeAsync(CancellationToken cancellationToken)
{
    using var udp = new UdpClient(AddressFamily.InterNetwork);
    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.Client.Bind(new IPEndPoint(IPAddress.Any, HomeBeacon.Port));
    while (true)
    {
        var received = await udp.ReceiveAsync(cancellationToken);
        if (HomeBeacon.TryParse(received.Buffer, out int port))
            return $"http://{received.RemoteEndPoint.Address}:{port}";
    }
}
