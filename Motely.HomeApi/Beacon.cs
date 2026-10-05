using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Motely.Distributed;

namespace Motely.HomeApi;

/// <summary>Broadcasts this server's port on the LAN (<see cref="HomeBeacon"/>) so workers need no address.</summary>
internal sealed class Beacon(IServer server, ILogger<Beacon> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            string? address;
            while ((address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()) is null)
                await Task.Delay(200, stoppingToken);
            int port = BindingAddress.Parse(address).Port;

            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            var message = HomeBeacon.Message(port);
            var to = new IPEndPoint(IPAddress.Broadcast, HomeBeacon.Port);
            log.LogInformation("Beacon: announcing port {Port} on UDP {UdpPort} every {Seconds}s", port, HomeBeacon.Port, Interval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await udp.SendAsync(message, to, stoppingToken);
                }
                catch (SocketException ex)
                {
                    log.LogWarning("Beacon send failed: {Message}", ex.Message);
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
