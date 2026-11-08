using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using HardwareCheck.Api.Models;

namespace HardwareCheck.Api.Services;

public class LhmDiscoveryAndMonitorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LocalLhmBridgeService _localBridge;
    private readonly HardwareRecordingService _recordingService;
    private readonly string _storagePath;
    private readonly object _configLock = new();
    private List<SavedEndpointConfig> _savedEndpoints = new();
    private bool _demoModeEnabled = true;
    private readonly Dictionary<string, DeviceDiscoveryAlert> _pendingAlerts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownSessionDevices = new(StringComparer.OrdinalIgnoreCase);

    public bool SelfListeningOn8085 { get; set; } = false;

    public LhmDiscoveryAndMonitorService(
        IHttpClientFactory httpClientFactory,
        LocalLhmBridgeService localBridge,
        HardwareRecordingService recordingService,
        IWebHostEnvironment env)
    {
        _httpClientFactory = httpClientFactory;
        _localBridge = localBridge;
        _recordingService = recordingService;

        string dataDir = Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        _storagePath = Path.Combine(dataDir, "endpoints.json");
        LoadConfig();
    }

    public bool DemoModeEnabled
    {
        get => _demoModeEnabled;
        set
        {
            _demoModeEnabled = value;
            SaveConfig();
        }
    }

    public async Task<DashboardResponseDto> GetDashboardDataAsync(CancellationToken ct = default)
    {
        var devices = new List<HardwareDeviceReportDto>();

        // 1. Localhost:8085 (either external LHM WebServer on 8085 or our built-in LocalBridge on 8085)
        var localDevice = await FetchLocalDeviceAsync(ct);
        devices.Add(localDevice);

        // 2. Saved / Discovered Remote Endpoints on Port 8085
        List<SavedEndpointConfig> endpointsSnapshot;
        List<SavedEndpointConfig> archivedSnapshot;
        lock (_configLock)
        {
            endpointsSnapshot = _savedEndpoints.Where(e => e.IsEnabled && !e.IsArchived).ToList();
            archivedSnapshot = _savedEndpoints.Where(e => e.IsEnabled && e.IsArchived).ToList();
        }

        if (endpointsSnapshot.Count > 0)
        {
            var tasks = endpointsSnapshot.Select(ep => FetchRemoteEndpointAsync(ep, ct));
            var remoteResults = await Task.WhenAll(tasks);
            devices.AddRange(remoteResults);
        }

        // Add archived endpoints without waiting for network timeouts
        foreach (var arch in archivedSnapshot)
        {
            devices.Add(new HardwareDeviceReportDto
            {
                Id = arch.Id,
                Name = arch.Name,
                DisplayLabel = string.IsNullOrWhiteSpace(arch.Name) ? $"{arch.Host}:{arch.Port}" : arch.Name,
                Host = arch.Host,
                Port = arch.Port,
                EndpointUrl = $"http://{arch.Host}:{arch.Port}/data.json",
                SourceType = "lhm-api",
                SourceBadge = "Archiviert",
                IsOnline = false,
                IsArchived = true,
                ArchivedAtDe = arch.ArchivedAtDe,
                OrderNumber = arch.OrderNumber,
                TestType = arch.TestType,
                ErrorMessage = $"Tests abgeschlossen ({arch.ArchivedAtDe ?? "Archiv"})"
            });
        }

        // 3. Demo Workstations (so user can inspect multi-PC LHM setups right away)
        if (_demoModeEnabled)
        {
            var demo1Tree = LocalLhmBridgeService.BuildDemoWorkstationTree("CPM-WORKSTATION-01", 1);
            var demo1 = LhmTreeParser.ParseTree(
                demo1Tree,
                id: "demo-ws-01",
                displayLabel: "CPM-WORKSTATION-01 (CAD & Technik)",
                host: "192.168.178.24",
                port: 8085,
                sourceType: "demo",
                latencyMs: 4,
                explicitOrderNumber: "AUF-88410",
                explicitTestType: "hardware-test");
            devices.Add(demo1);

            var demo2Tree = LocalLhmBridgeService.BuildDemoWorkstationTree("CPM-PRUEFSTAND-02", 2);
            var demo2 = LhmTreeParser.ParseTree(
                demo2Tree,
                id: "demo-ws-02",
                displayLabel: "CPM-PRUEFSTAND-02 (Akku-Prüfstand)",
                host: "192.168.178.38",
                port: 8085,
                sourceType: "demo",
                latencyMs: 7,
                explicitOrderNumber: "AUF-90123",
                explicitTestType: "battery-test");
            demo2.HasBattery = true;
            demo2.BatteryLevelPercent = 78.5;
            demo2.BatteryWearPercent = 4.2;
            devices.Add(demo2);
        }

        // Apply simulated crash / connection drop if active for any device
        foreach (var d in devices)
        {
            if (_recordingService.IsDeviceSimulatedOffline(d.Id))
            {
                d.IsOnline = false;
                d.ErrorMessage = $"Verbindung abgebrochen / Absturz (Keine Antwort von {d.Host}:{d.Port})";
            }
        }

        // Record telemetry & detect any connection drops ("Absturz") in German timezone
        _recordingService.ProcessDashboardSnapshot(devices);

        return new DashboardResponseDto
        {
            Timestamp = DateTime.UtcNow,
            TimestampDe = GermanTimeHelper.FormatFull(),
            ListenerPort = 8085,
            LocalPort8085Active = localDevice.IsOnline,
            DemoModeEnabled = _demoModeEnabled,
            IsRecording = _recordingService.IsRecording,
            TotalCrashesLogged = _recordingService.CrashCount,
            OnlineDevicesCount = devices.Count(d => d.IsOnline),
            TotalHardwareComponentsCount = devices.Where(d => d.IsOnline).Sum(d => d.TotalHardwareCount),
            TotalSensorsCount = devices.Where(d => d.IsOnline).Sum(d => d.TotalSensorCount),
            Devices = devices,
            DiscoveryAlerts = GetPendingDiscoveryAlerts()
        };
    }

    public async Task<HardwareDeviceReportDto?> GetDeviceByIdAsync(string id, CancellationToken ct = default)
    {
        var dashboard = await GetDashboardDataAsync(ct);
        return dashboard.Devices.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HardwareDeviceReportDto> FetchLocalDeviceAsync(CancellationToken ct)
    {
        // If an external Libre Hardware Monitor GUI Web Server is listening on 8085 (i.e. we did NOT bind 8085 ourselves),
        // try querying http://127.0.0.1:8085/data.json first!
        if (!SelfListeningOn8085)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var client = _httpClientFactory.CreateClient("LhmClient");
                using var resp = await client.GetAsync("http://127.0.0.1:8085/data.json", ct);
                if (resp.IsSuccessStatusCode)
                {
                    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    var tree = await JsonSerializer.DeserializeAsync<LhmNode>(stream, cancellationToken: ct);
                    sw.Stop();
                    if (tree != null)
                    {
                        var dev = LhmTreeParser.ParseTree(
                            tree,
                            id: "local-8085",
                            displayLabel: $"{Environment.MachineName} (LibreHardwareMonitor :8085)",
                            host: "127.0.0.1",
                            port: 8085,
                            sourceType: "lhm-api",
                            latencyMs: Math.Max(1, sw.ElapsedMilliseconds));
                        dev.StressStatus = await FetchStressStatusAsync("127.0.0.1", 8085, ct);
                        return dev;
                    }
                }
            }
            catch
            {
                // Fall back to local hardware bridge
            }
        }

        var swLocal = Stopwatch.StartNew();
        var localTree = _localBridge.BuildLocalLhmTree();
        swLocal.Stop();

        var localDev = LhmTreeParser.ParseTree(
            localTree,
            id: "local-8085",
            displayLabel: $"{Environment.MachineName} (Dieser Rechner • Port 8085)",
            host: "127.0.0.1",
            port: 8085,
            sourceType: "local-bridge",
            latencyMs: Math.Max(1, swLocal.ElapsedMilliseconds));
        localDev.StressStatus = await FetchStressStatusAsync("127.0.0.1", 8085, ct);
        return localDev;
    }

    private async Task<HardwareDeviceReportDto> FetchRemoteEndpointAsync(SavedEndpointConfig ep, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string url = $"http://{ep.Host}:{ep.Port}/data.json";
        try
        {
            var client = _httpClientFactory.CreateClient("LhmClient");
            using var resp = await client.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var tree = await JsonSerializer.DeserializeAsync<LhmNode>(stream, cancellationToken: ct);
            sw.Stop();

            if (tree != null)
            {
                var dev = LhmTreeParser.ParseTree(
                    tree,
                    id: ep.Id,
                    displayLabel: string.IsNullOrWhiteSpace(ep.Name) ? $"{ep.Host}:{ep.Port}" : ep.Name,
                    host: ep.Host,
                    port: ep.Port,
                    sourceType: "lhm-api",
                    latencyMs: Math.Max(1, sw.ElapsedMilliseconds),
                    explicitOrderNumber: ep.OrderNumber,
                    explicitTestType: ep.TestType);

                dev.StressStatus = await FetchStressStatusAsync(ep.Host, ep.Port, ct);
                dev.IsArchived = ep.IsArchived;
                dev.ArchivedAtDe = ep.ArchivedAtDe;
                return dev;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HardwareDeviceReportDto
            {
                Id = ep.Id,
                Name = string.IsNullOrWhiteSpace(ep.Name) ? ep.Host : ep.Name,
                DisplayLabel = string.IsNullOrWhiteSpace(ep.Name) ? $"{ep.Host}:{ep.Port}" : ep.Name,
                Host = ep.Host,
                Port = ep.Port,
                EndpointUrl = url,
                SourceType = "lhm-api",
                SourceBadge = $"LHM API (:{ep.Port})",
                IsOnline = false,
                LatencyMs = sw.ElapsedMilliseconds,
                LastUpdated = DateTime.UtcNow,
                OrderNumber = ep.OrderNumber,
                TestType = ep.TestType,
                IsArchived = ep.IsArchived,
                ArchivedAtDe = ep.ArchivedAtDe,
                ErrorMessage = $"Keine Antwort auf Port {ep.Port} ({ex.GetType().Name})"
            };
        }

        return new HardwareDeviceReportDto
        {
            Id = ep.Id,
            Name = ep.Host,
            DisplayLabel = $"{ep.Host}:{ep.Port}",
            Host = ep.Host,
            Port = ep.Port,
            EndpointUrl = url,
            SourceType = "lhm-api",
            IsOnline = false,
            OrderNumber = ep.OrderNumber,
            TestType = ep.TestType,
            ErrorMessage = "Ungültiges JSON-Format von /data.json"
        };
    }

    private async Task<DeviceStressStatusDto?> FetchStressStatusAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(650));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var client = _httpClientFactory.CreateClient("LhmClient");
            using var resp = await client.GetAsync($"http://{host}:{port}/api/stress", linkedCts.Token);
            if (resp.IsSuccessStatusCode)
            {
                await using var s = await resp.Content.ReadAsStreamAsync(linkedCts.Token);
                return await JsonSerializer.DeserializeAsync<DeviceStressStatusDto>(s, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }, linkedCts.Token);
            }
        }
        catch { }
        return null;
    }

    public SavedEndpointConfig? GetEndpointById(string id)
    {
        lock (_configLock)
        {
            return _savedEndpoints.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public SavedEndpointConfig AddEndpoint(AddDeviceRequest req)
    {
        string cleanHost = (req.Host ?? string.Empty).Trim()
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/data.json", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        int port = req.Port > 0 ? req.Port : 8085;
        if (cleanHost.Contains(':'))
        {
            var parts = cleanHost.Split(':');
            cleanHost = parts[0];
            if (int.TryParse(parts[1], out int parsedPort) && parsedPort > 0)
                port = parsedPort;
        }

        lock (_configLock)
        {
            var existing = _savedEndpoints.FirstOrDefault(e =>
                e.Host.Equals(cleanHost, StringComparison.OrdinalIgnoreCase) && e.Port == port);

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(req.Name))
                    existing.Name = req.Name.Trim();
                if (!string.IsNullOrWhiteSpace(req.OrderNumber))
                    existing.OrderNumber = req.OrderNumber.Trim();
                if (!string.IsNullOrWhiteSpace(req.TestType))
                    existing.TestType = req.TestType.Trim();
                SaveConfig();
                return existing;
            }

            var created = new SavedEndpointConfig
            {
                Id = $"dev-{Guid.NewGuid().ToString("N")[..6]}",
                Name = string.IsNullOrWhiteSpace(req.Name) ? $"LHM Gerät ({cleanHost})" : req.Name.Trim(),
                Host = cleanHost,
                Port = port,
                IsEnabled = true,
                IsAutoDiscovered = false,
                OrderNumber = req.OrderNumber?.Trim(),
                TestType = req.TestType?.Trim()
            };

            _savedEndpoints.Add(created);
            SaveConfig();
            return created;
        }
    }

    public bool RemoveEndpoint(string id)
    {
        lock (_configLock)
        {
            int removed = _savedEndpoints.RemoveAll(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SaveConfig();
                _pendingAlerts.Remove(id);
                return true;
            }
            return false;
        }
    }

    public async Task<SavedEndpointConfig?> ArchiveEndpointAsync(string id, CancellationToken ct = default)
    {
        SavedEndpointConfig? endpoint = null;
        string? host = null;
        int port = 8085;

        lock (_configLock)
        {
            endpoint = _savedEndpoints.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (endpoint != null)
            {
                endpoint.IsArchived = true;
                endpoint.ArchivedAt = DateTime.UtcNow;
                endpoint.ArchivedAtDe = GermanTimeHelper.FormatFull();
                host = endpoint.Host;
                port = endpoint.Port;
                SaveConfig();
                _pendingAlerts.Remove(id);
            }
        }

        if (endpoint != null)
        {
            // 1. Send remote exit command to DiagnoseTool to close the application on the test laptop
            if (!string.IsNullOrWhiteSpace(host))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("LhmClient");
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(1500);
                    using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                    await client.PostAsync($"http://{host}:{port}/api/control/app/exit", content, cts.Token);
                }
                catch
                {
                    // Ignore if client is already closed or unreachable
                }
            }

            // 2. Log event in recording protocol
            _recordingService.AddCustomProtocolEvent(
                id,
                endpoint.Name,
                endpoint.OrderNumber,
                "ARCHIV",
                "Tests abgeschlossen",
                $"Tests für '{endpoint.Name}' abgeschlossen. DiagnoseTool beendet und Gerät archiviert.");
        }

        return endpoint;
    }

    public SavedEndpointConfig? RestoreEndpoint(string id)
    {
        lock (_configLock)
        {
            var endpoint = _savedEndpoints.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (endpoint != null)
            {
                endpoint.IsArchived = false;
                endpoint.ArchivedAt = null;
                endpoint.ArchivedAtDe = null;
                SaveConfig();
                _recordingService.AddCustomProtocolEvent(
                    id,
                    endpoint.Name,
                    endpoint.OrderNumber,
                    "ARCHIV",
                    "Gerät wiederhergestellt",
                    $"Gerät '{endpoint.Name}' wurde aus dem Archiv wiederhergestellt.");
                return endpoint;
            }
            return null;
        }
    }

    public (SavedEndpointConfig endpoint, bool isNew) RegisterDiscoveredEndpoint(
        string host,
        int port,
        string displayName,
        string? orderNumber,
        string? systemModel)
    {
        string cleanHost = host.Trim();
        if (cleanHost.Contains(':'))
        {
            var parts = cleanHost.Split(':');
            cleanHost = parts[0];
            if (int.TryParse(parts[1], out int parsedPort) && parsedPort > 0)
                port = parsedPort;
        }

        SavedEndpointConfig endpoint;
        bool isNew = false;
        string deviceKey = $"{cleanHost}:{port}".ToLowerInvariant();

        lock (_configLock)
        {
            var existing = _savedEndpoints.FirstOrDefault(e =>
                e.Host.Equals(cleanHost, StringComparison.OrdinalIgnoreCase) && e.Port == port);

            if (existing != null)
            {
                endpoint = existing;
                if (!string.IsNullOrWhiteSpace(displayName))
                    existing.Name = displayName.Trim();
                if (!string.IsNullOrWhiteSpace(orderNumber))
                    existing.OrderNumber = orderNumber.Trim();
            }
            else
            {
                isNew = true;
                endpoint = new SavedEndpointConfig
                {
                    Id = $"dev-{Guid.NewGuid().ToString("N")[..6]}",
                    Name = displayName,
                    Host = cleanHost,
                    Port = port,
                    IsEnabled = true,
                    IsAutoDiscovered = true,
                    OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? null : orderNumber.Trim(),
                    TestType = "combined"
                };
                _savedEndpoints.Add(endpoint);
            }
            SaveConfig();

            bool isFirstTimeInSession = _knownSessionDevices.Add(deviceKey);
            // Trigger alert if it's a new device or seen for the first time in this session or missing an order number
            if (isNew || isFirstTimeInSession || string.IsNullOrWhiteSpace(endpoint.OrderNumber))
            {
                if (!_pendingAlerts.ContainsKey(endpoint.Id))
                {
                    _pendingAlerts[endpoint.Id] = new DeviceDiscoveryAlert
                    {
                        AlertId = Guid.NewGuid().ToString("N")[..8],
                        DeviceId = endpoint.Id,
                        Host = endpoint.Host,
                        Port = endpoint.Port,
                        Name = endpoint.Name,
                        SystemModel = systemModel,
                        CurrentOrderNumber = endpoint.OrderNumber,
                        DiscoveredAt = DateTime.UtcNow,
                        DiscoveredAtDe = GermanTimeHelper.FormatFull(),
                        Message = "Neues Gerät verfügbar"
                    };
                }
            }
        }

        return (endpoint, isNew);
    }

    public List<DeviceDiscoveryAlert> GetPendingDiscoveryAlerts()
    {
        lock (_configLock)
        {
            return _pendingAlerts.Values.OrderByDescending(a => a.DiscoveredAt).ToList();
        }
    }

    public bool DismissDiscoveryAlert(string deviceIdOrAlertId)
    {
        lock (_configLock)
        {
            var pair = _pendingAlerts.FirstOrDefault(kvp =>
                kvp.Key.Equals(deviceIdOrAlertId, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.AlertId.Equals(deviceIdOrAlertId, StringComparison.OrdinalIgnoreCase));

            if (pair.Key != null)
            {
                _pendingAlerts.Remove(pair.Key);
                return true;
            }
            return false;
        }
    }

    public async Task<SavedEndpointConfig?> UpdateEndpointOrderNumberAsync(string deviceId, string orderNumber, CancellationToken ct = default)
    {
        SavedEndpointConfig? endpoint = null;
        string? host = null;
        int port = 8085;

        lock (_configLock)
        {
            endpoint = _savedEndpoints.FirstOrDefault(e => e.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
            if (endpoint != null)
            {
                endpoint.OrderNumber = orderNumber?.Trim() ?? string.Empty;
                host = endpoint.Host;
                port = endpoint.Port;
                SaveConfig();
                DismissDiscoveryAlert(deviceId);
            }
        }

        if (endpoint != null && !string.IsNullOrWhiteSpace(host))
        {
            try
            {
                var client = _httpClientFactory.CreateClient("LhmClient");
                string json = JsonSerializer.Serialize(new { orderNumber = endpoint.OrderNumber });
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(1500);
                await client.PostAsync($"http://{host}:{port}/api/control/order", content, cts.Token);
            }
            catch
            {
                // Ignore if client is unreachable
            }
        }

        return endpoint;
    }

    public async Task<ScanNetworkResponse> ScanNetworkForPort8085Async(ScanNetworkRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int port = req.Port > 0 ? req.Port : 8085;
        int timeoutMs = Math.Clamp(req.TimeoutMs > 0 ? req.TimeoutMs : 300, 120, 1500);

        string subnetPrefix = !string.IsNullOrWhiteSpace(req.SubnetPrefix)
            ? req.SubnetPrefix.Trim().TrimEnd('.')
            : DetectLocalSubnetPrefix() ?? "192.168.178";

        // Normalize if full IP was entered (e.g. 192.168.178.1 -> 192.168.178)
        var octets = subnetPrefix.Split('.');
        if (octets.Length == 4)
        {
            subnetPrefix = $"{octets[0]}.{octets[1]}.{octets[2]}";
        }

        var discovered = new List<HardwareDeviceReportDto>();

        // Always check 127.0.0.1 first
        var localDev = await FetchLocalDeviceAsync(ct);
        if (localDev.IsOnline)
        {
            discovered.Add(localDev);
        }

        // Scan .1 to .254 in parallel
        var semaphore = new SemaphoreSlim(64);
        var localIps = GetLocalIpAddresses();
        var probeTasks = new List<Task<HardwareDeviceReportDto?>>();

        for (int i = 1; i <= 254; i++)
        {
            string ip = $"{subnetPrefix}.{i}";
            if (localIps.Contains(ip)) continue;

            probeTasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (!await IsTcpPortOpenAsync(ip, port, timeoutMs, ct))
                        return null;

                    var ep = new SavedEndpointConfig
                    {
                        Id = $"scan-{ip.Replace('.', '-')}",
                        Name = $"Netzwerk-PC ({ip})",
                        Host = ip,
                        Port = port,
                        IsAutoDiscovered = true
                    };

                    var report = await FetchRemoteEndpointAsync(ep, ct);
                    if (report.IsOnline)
                    {
                        // Persist discovered LHM device automatically
                        AddEndpoint(new AddDeviceRequest
                        {
                            Name = report.Name,
                            Host = ip,
                            Port = port
                        });
                        return report;
                    }
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        var probeResults = await Task.WhenAll(probeTasks);
        discovered.AddRange(probeResults.Where(r => r != null)!);
        sw.Stop();

        return new ScanNetworkResponse
        {
            ScannedRange = $"{subnetPrefix}.1 - {subnetPrefix}.254 (+ 127.0.0.1)",
            Port = port,
            HostsScanned = 255,
            DurationMs = (int)sw.ElapsedMilliseconds,
            DiscoveredDevices = discovered
        };
    }

    public static string? DetectLocalSubnetPrefix()
    {
        // 1. Priorisiere Umgebungsvariable (besonders wichtig für Docker-Container im Bridge-Modus)
        string? envSubnet = Environment.GetEnvironmentVariable("SCAN_SUBNET")
            ?? Environment.GetEnvironmentVariable("HARDWARE_CHECK_SUBNET");
        if (!string.IsNullOrWhiteSpace(envSubnet))
        {
            var parts = envSubnet.Trim().TrimEnd('.').Split('.');
            if (parts.Length >= 3)
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
        }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = ua.Address.ToString();

                        // Ignoriere typische interne Docker-Bridge Subnetze (172.17.x, 172.18.x, 172.19.x etc.)
                        if (ip.StartsWith("172.17.") || ip.StartsWith("172.18.") || ip.StartsWith("172.19.") ||
                            ip.StartsWith("172.20.") || ip.StartsWith("172.21.") || ip.StartsWith("172.22."))
                            continue;

                        if (ip.StartsWith("192.168.") || ip.StartsWith("10."))
                        {
                            var parts = ip.Split('.');
                            if (parts.Length == 4)
                                return $"{parts[0]}.{parts[1]}.{parts[2]}";
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }
        return "192.168.178";
    }

    public static string GetPrimaryHostIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("172.17.") || ip.StartsWith("172.18.") || ip.StartsWith("172.19."))
                            continue;
                        if (ip.StartsWith("192.168.") || ip.StartsWith("10."))
                            return ip;
                    }
                }
            }
        }
        catch { }

        string? subnet = DetectLocalSubnetPrefix();
        return subnet != null ? $"{subnet}.1" : "127.0.0.1";
    }

    private static HashSet<string> GetLocalIpAddresses()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost" };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        set.Add(ua.Address.ToString());
                }
            }
        }
        catch
        {
            // Ignore
        }
        return set;
    }

    private static async Task<bool> IsTcpPortOpenAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, linkedCts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private class PersistedState
    {
        public bool DemoModeEnabled { get; set; } = true;
        public List<SavedEndpointConfig> Endpoints { get; set; } = new();
    }

    private void LoadConfig()
    {
        lock (_configLock)
        {
            if (!File.Exists(_storagePath)) return;
            try
            {
                string json = File.ReadAllText(_storagePath);
                var state = JsonSerializer.Deserialize<PersistedState>(json);
                if (state != null)
                {
                    _demoModeEnabled = state.DemoModeEnabled;
                    _savedEndpoints = state.Endpoints ?? new();
                }
            }
            catch
            {
                // Keep defaults
            }
        }
    }

    private void SaveConfig()
    {
        lock (_configLock)
        {
            try
            {
                var state = new PersistedState
                {
                    DemoModeEnabled = _demoModeEnabled,
                    Endpoints = _savedEndpoints
                };
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
            }
            catch
            {
                // Ignore write error
            }
        }
    }
}

