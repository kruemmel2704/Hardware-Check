using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HardwareCheck.Api.Models;

namespace HardwareCheck.Api.Services;

public class UdpDiscoveryListenerService : IHostedService, IDisposable
{
    public const int DiscoveryPort = 5087;

    private readonly LhmDiscoveryAndMonitorService _monitorService;
    private readonly ILogger<UdpDiscoveryListenerService> _logger;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public UdpDiscoveryListenerService(
        LhmDiscoveryAndMonitorService monitorService,
        ILogger<UdpDiscoveryListenerService> logger)
    {
        _monitorService = monitorService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.EnableBroadcast = true;
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _logger.LogInformation("[UdpDiscoveryListener] Bound successfully to UDP port {Port}", DiscoveryPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[UdpDiscoveryListener] Direct bind on port {Port} failed ({Message}), creating ephemeral UDP client.", DiscoveryPort, ex.Message);
            try
            {
                _udpClient = new UdpClient();
                _udpClient.EnableBroadcast = true;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "[UdpDiscoveryListener] Could not initialize UDP client.");
                return Task.CompletedTask;
            }
        }

        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
            if (_listenerTask != null)
            {
                await Task.WhenAny(_listenerTask, Task.Delay(1000, cancellationToken));
            }
        }
        catch { }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("[UdpDiscoveryListener] Autodiscovery listener loop started.");

        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                string json = Encoding.UTF8.GetString(result.Buffer);

                if (string.IsNullOrWhiteSpace(json)) continue;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) continue;

                string type = typeProp.GetString() ?? "";

                if (string.Equals(type, "DIAGNOSE_TOOL_DISCOVER", StringComparison.OrdinalIgnoreCase))
                {
                    string host = root.TryGetProperty("host", out var hProp) ? (hProp.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(host) || host == "127.0.0.1")
                    {
                        host = result.RemoteEndPoint.Address.ToString();
                        // Normalize IPv4 mapped IPv6 (e.g. ::ffff:192.168.178.50)
                        if (host.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
                        {
                            host = host.Substring(7);
                        }
                    }

                    int port = root.TryGetProperty("port", out var pProp) && pProp.TryGetInt32(out int p) ? p : 8085;
                    string machineName = root.TryGetProperty("machineName", out var mProp) ? (mProp.GetString() ?? "CPM-Gerät") : "CPM-Gerät";
                    string orderNumber = root.TryGetProperty("orderNumber", out var oProp) ? (oProp.GetString() ?? "") : "";
                    string systemModel = root.TryGetProperty("systemModel", out var sProp) ? (sProp.GetString() ?? "") : "";

                    string displayName = !string.IsNullOrWhiteSpace(systemModel)
                        ? $"{machineName} ({systemModel})"
                        : machineName;

                    _logger.LogInformation("[UdpDiscoveryListener] Discovered DiagnoseTool from {Host}:{Port} ({Machine}, Auftrag: {Order})",
                        host, port, machineName, orderNumber);

                    // Register endpoint in monitoring service
                    _monitorService.AddEndpoint(new AddDeviceRequest
                    {
                        Host = host,
                        Port = port,
                        Name = displayName,
                        OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? null : orderNumber,
                        TestType = "combined"
                    });

                    // Send HARDWARE_CHECK_ACK back to the client
                    string localIp = LhmDiscoveryAndMonitorService.DetectLocalSubnetPrefix() != null
                        ? GetPrimaryLocalIp()
                        : "127.0.0.1";

                    var ack = new
                    {
                        type = "HARDWARE_CHECK_ACK",
                        version = "1.0",
                        serverHost = localIp,
                        serverPort = 5085,
                        clientHost = host,
                        message = "Connected to CPM Hardware-Check Server",
                        timestamp = DateTime.UtcNow.ToString("o")
                    };

                    byte[] ackBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ack));

                    // Send directly to sender endpoint
                    await _udpClient.SendAsync(ackBytes, ackBytes.Length, result.RemoteEndPoint);

                    // Also send to port 5087 on sender's IP
                    try
                    {
                        await _udpClient.SendAsync(ackBytes, ackBytes.Length, new IPEndPoint(result.RemoteEndPoint.Address, DiscoveryPort));
                    }
                    catch { }

                    // Also send to 127.0.0.1 if client is local
                    if (result.RemoteEndPoint.Address.Equals(IPAddress.Loopback) || host == "127.0.0.1")
                    {
                        try
                        {
                            await _udpClient.SendAsync(ackBytes, ackBytes.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[UdpDiscoveryListener] Packet parse or receive error: {Message}", ex.Message);
            }
        }
    }

    public async Task BroadcastPingAsync(CancellationToken ct = default)
    {
        if (_udpClient == null) return;

        try
        {
            string localIp = GetPrimaryLocalIp();
            var ping = new
            {
                type = "HARDWARE_CHECK_PING",
                serverHost = localIp,
                serverPort = 5085,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ping));
            var targets = GetBroadcastTargets();

            foreach (var target in targets)
            {
                try
                {
                    await _udpClient.SendAsync(bytes, bytes.Length, target);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("[UdpDiscoveryListener] Ping to {Target} failed: {Message}", target, ex.Message);
                }
            }

            _logger.LogInformation("[UdpDiscoveryListener] Sent HARDWARE_CHECK_PING to {Count} broadcast targets.", targets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[UdpDiscoveryListener] BroadcastPingAsync error: {Message}", ex.Message);
        }
    }

    private List<IPEndPoint> GetBroadcastTargets()
    {
        var targets = new List<IPEndPoint>
        {
            new IPEndPoint(IPAddress.Broadcast, DiscoveryPort),          // 255.255.255.255
            new IPEndPoint(IPAddress.Loopback, DiscoveryPort)            // 127.0.0.1
        };

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipProps = ni.GetIPProperties();
                foreach (var u in ipProps.UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipBytes = u.Address.GetAddressBytes();
                        var maskBytes = u.IPv4Mask?.GetAddressBytes();
                        if (maskBytes != null && maskBytes.Length == 4)
                        {
                            var bcBytes = new byte[4];
                            for (int i = 0; i < 4; i++)
                            {
                                bcBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                            }
                            var bcIp = new IPAddress(bcBytes);
                            if (!targets.Exists(t => t.Address.Equals(bcIp)))
                            {
                                targets.Add(new IPEndPoint(bcIp, DiscoveryPort));
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return targets;
    }

    private static string GetPrimaryLocalIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = ni.GetIPProperties();
                foreach (var ip in props.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
        _cts?.Dispose();
    }
}

