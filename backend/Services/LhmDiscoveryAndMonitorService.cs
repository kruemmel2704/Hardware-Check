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
    private bool _includeLocalDevice = false; // Das Grundgerät (Server selbst) standardmäßig nicht in der Auflistung anzeigen
    private readonly Dictionary<string, DeviceDiscoveryAlert> _pendingAlerts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownSessionDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _frozenDir;
    private readonly string _batteryTestsPath;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HardwareDeviceReportDto> _frozenReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BatteryTestSessionState> _batteryTests = new(StringComparer.OrdinalIgnoreCase);

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
        _batteryTestsPath = Path.Combine(dataDir, "battery_tests.json");
        string frozenDir = Path.Combine(dataDir, "frozen_reports");
        Directory.CreateDirectory(frozenDir);
        _frozenDir = frozenDir;
        LoadConfig();
        LoadBatteryTests();
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

    public bool IncludeLocalDevice
    {
        get => _includeLocalDevice;
        set
        {
            lock (_configLock)
            {
                _includeLocalDevice = value;
                SaveConfig();
            }
        }
    }

    public async Task<DashboardResponseDto> GetDashboardDataAsync(CancellationToken ct = default)
    {
        var devices = new List<HardwareDeviceReportDto>();

        // 1. Localhost:8085 (Grundgerät / Server selbst) - nur hinzufügen wenn explizit aktiviert
        if (_includeLocalDevice)
        {
            var localDevice = await FetchLocalDeviceAsync(ct);
            devices.Add(localDevice);
        }

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

        // Add archived endpoints with their frozen values preserved
        foreach (var arch in archivedSnapshot)
        {
            var frozen = GetOrLoadFrozenReport(arch);
            devices.Add(frozen);
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

            var (demo2CustomPct, demo2CustomWatts) = GetSimulatedDemoBatteryValues("demo-ws-02");
            var demo2Tree = LocalLhmBridgeService.BuildDemoWorkstationTree("CPM-PRUEFSTAND-02", 2, demo2CustomPct, demo2CustomWatts);
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
            devices.Add(demo2);
        }

        // Apply simulated crash / connection drop if active for any device & attach battery test state
        foreach (var d in devices)
        {
            if (_recordingService.IsDeviceSimulatedOffline(d.Id))
            {
                d.IsOnline = false;
                d.ErrorMessage = $"Verbindung abgebrochen / Absturz (Keine Antwort von {d.Host}:{d.Port})";
            }

            UpdateAndAttachBatteryTestState(d);
        }

        // Record telemetry & detect any connection drops ("Absturz") in German timezone
        _recordingService.ProcessDashboardSnapshot(devices);

        return new DashboardResponseDto
        {
            Timestamp = DateTime.UtcNow,
            TimestampDe = GermanTimeHelper.FormatFull(),
            ListenerPort = 8085,
            LocalPort8085Active = SelfListeningOn8085,
            IncludeLocalDevice = _includeLocalDevice,
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
                        await EnrichFromDiagnoseToolTelemetryAsync(dev, "127.0.0.1", 8085, ct);
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
                await EnrichFromDiagnoseToolTelemetryAsync(dev, ep.Host, ep.Port, ct);
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
                DeleteFrozenReport(id);
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

        // 1. In der Aufzeichnung markieren, damit kein "Absturz" ausgelöst wird
        _recordingService.MarkDeviceArchived(id);

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
            // 2. Aktuelle Live-Messwerte sofort abrufen und als "eingefroren" festhalten
            HardwareDeviceReportDto? liveReport = null;
            if (!string.IsNullOrWhiteSpace(host))
            {
                try
                {
                    using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    probeCts.CancelAfter(2500);
                    liveReport = await FetchRemoteEndpointAsync(endpoint, probeCts.Token);
                }
                catch { }
            }

            if (liveReport != null && (liveReport.TotalHardwareCount > 0 || liveReport.TotalSensorCount > 0 || liveReport.Components.Count > 0))
            {
                liveReport.IsArchived = true;
                liveReport.IsOnline = false;
                liveReport.ArchivedAtDe = endpoint.ArchivedAtDe;
                liveReport.SourceBadge = "Archiviert";
                liveReport.ErrorMessage = $"Tests abgeschlossen ({endpoint.ArchivedAtDe})";
                SaveFrozenReport(id, liveReport);
            }

            // 3. Remote Exit-Befehl an das DiagnoseTool senden
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

            // 4. Protokoll-Eintrag für sauberen Testabschluss
            _recordingService.AddCustomProtocolEvent(
                id,
                endpoint.Name,
                endpoint.OrderNumber,
                "ARCHIV",
                "Tests abgeschlossen",
                $"Tests für '{endpoint.Name}' abgeschlossen. Messwerte zum Abschlusszeitpunkt ({endpoint.ArchivedAtDe}) eingefroren und DiagnoseTool beendet.");
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
                DeleteFrozenReport(id);
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

        // Check 127.0.0.1 nur wenn Grundgerät explizit aktiviert ist
        if (_includeLocalDevice)
        {
            var localDev = await FetchLocalDeviceAsync(ct);
            if (localDev.IsOnline)
            {
                discovered.Add(localDev);
            }
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
        public bool IncludeLocalDevice { get; set; } = false;
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
                    _includeLocalDevice = state.IncludeLocalDevice;
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
                    IncludeLocalDevice = _includeLocalDevice,
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

    private void SaveFrozenReport(string id, HardwareDeviceReportDto report)
    {
        try
        {
            _frozenReports[id] = report;
            string path = Path.Combine(_frozenDir, $"{id}.json");
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(path, json);
        }
        catch { }
    }

    private void DeleteFrozenReport(string id)
    {
        try
        {
            _frozenReports.TryRemove(id, out _);
            string path = Path.Combine(_frozenDir, $"{id}.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private HardwareDeviceReportDto GetOrLoadFrozenReport(SavedEndpointConfig arch)
    {
        if (_frozenReports.TryGetValue(arch.Id, out var cached))
        {
            return cached;
        }

        string path = Path.Combine(_frozenDir, $"{arch.Id}.json");
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var report = JsonSerializer.Deserialize<HardwareDeviceReportDto>(json);
                if (report != null)
                {
                    report.IsArchived = true;
                    report.IsOnline = false;
                    report.ArchivedAtDe = arch.ArchivedAtDe;
                    report.SourceBadge = "Archiviert";
                    report.ErrorMessage = $"Tests abgeschlossen ({arch.ArchivedAtDe ?? "Archiv"})";
                    _frozenReports[arch.Id] = report;
                    return report;
                }
            }
            catch { }
        }

        return new HardwareDeviceReportDto
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
        };
    }

    #region Battery Test Management & DiagnoseTool Telemetry

    public async Task<BatteryTestStatusDto?> StartBatteryTestAsync(string deviceId, StartBatteryTestRequest req, CancellationToken ct = default)
    {
        var device = await GetDeviceByIdAsync(deviceId, ct);
        if (device == null) return null;

        int durationMin = Math.Clamp(req.DurationMinutes > 0 ? req.DurationMinutes : 30, 5, 240);
        string loadMode = string.IsNullOrWhiteSpace(req.LoadMode) ? "youtube" : req.LoadMode.ToLowerInvariant();
        string loadModeDisplay = loadMode switch
        {
            "cpu" => "CPU-Volllast Stresstest",
            "full" => "Volllast (CPU + FurMark GPU)",
            "idle" => "Standard-Entladung (Idle/Office)",
            _ => "YouTube 4K HDR Stress-Test"
        };

        double currentCharge = device.Battery?.ChargeLevelPercent ?? device.BatteryLevelPercent ?? 100.0;
        double? health = device.Battery?.HealthPercent;
        double? wear = device.Battery?.WearPercent;

        var session = new BatteryTestSessionState
        {
            DeviceId = deviceId,
            DeviceName = device.Name,
            IsRunning = true,
            IsCompleted = false,
            LoadMode = loadMode,
            LoadModeDisplay = loadModeDisplay,
            DurationMinutes = durationMin,
            StartedAt = DateTime.UtcNow,
            StartedAtDe = GermanTimeHelper.FormatFull(),
            StartChargePercent = currentCharge,
            CurrentChargePercent = currentCharge,
            EndChargePercent = currentCharge,
            HealthPercent = health,
            WearPercent = wear,
            ConnectedToAcWarning = device.Battery?.IsConnectedToAc == true,
            Verdict = "LAUFEND",
            VerdictText = $"Batterietest läuft ({loadModeDisplay}, {durationMin} Min.)",
            LogEntries = new List<string>
            {
                $"[{GermanTimeHelper.FormatTimeOnly()}] - Batterietest gestartet bei {currentCharge:F1}% Ladung ({loadModeDisplay}, Dauer: {durationMin} Min.)"
            }
        };

        if (session.ConnectedToAcWarning)
        {
            session.LogEntries.Add($"[{GermanTimeHelper.FormatTimeOnly()}] - Warnung: Netzteil angeschlossen! Für verlässliche Entladewerte bitte Ladekabel trennen.");
        }

        _batteryTests[deviceId] = session;
        SaveBatteryTests();

        // Remote DiagnoseTool control if online
        if (device.SourceType == "lhm-api" && !string.IsNullOrWhiteSpace(device.Host))
        {
            try
            {
                var client = _httpClientFactory.CreateClient("LhmClient");
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(2000);

                // Start YouTube or stress test
                if (loadMode == "youtube")
                {
                    using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                    await client.PostAsync($"http://{device.Host}:{device.Port}/api/control/stress/battery/start", content, timeoutCts.Token);
                }
                else if (loadMode == "cpu")
                {
                    string cpuJson = JsonSerializer.Serialize(new { durationMinutes = durationMin });
                    using var content = new StringContent(cpuJson, System.Text.Encoding.UTF8, "application/json");
                    await client.PostAsync($"http://{device.Host}:{device.Port}/api/control/stress/cpu/start", content, timeoutCts.Token);
                }
                else if (loadMode == "full")
                {
                    string cpuJson = JsonSerializer.Serialize(new { durationMinutes = durationMin });
                    using var contentCpu = new StringContent(cpuJson, System.Text.Encoding.UTF8, "application/json");
                    await client.PostAsync($"http://{device.Host}:{device.Port}/api/control/stress/cpu/start", contentCpu, timeoutCts.Token);
                    using var contentGpu = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                    await client.PostAsync($"http://{device.Host}:{device.Port}/api/control/stress/gpu/donut", contentGpu, timeoutCts.Token);
                }

                if (req.AutoRecord)
                {
                    string recJson = JsonSerializer.Serialize(new { orderNumber = device.OrderNumber });
                    using var content = new StringContent(recJson, System.Text.Encoding.UTF8, "application/json");
                    await client.PostAsync($"http://{device.Host}:{device.Port}/api/control/recording/start", content, timeoutCts.Token);
                }
            }
            catch { }
        }

        _recordingService.AddCustomProtocolEvent(
            deviceId,
            device.Name,
            device.OrderNumber,
            "BATTERIE",
            "Batterietest gestartet",
            $"Batterie-Stresstest ({loadModeDisplay}, {durationMin} Min.) für '{device.Name}' gestartet. Anfangsladung: {currentCharge:F1}%.");

        return MapSessionToDto(session);
    }

    public async Task<BatteryTestStatusDto?> StopBatteryTestAsync(string deviceId, CancellationToken ct = default)
    {
        if (!_batteryTests.TryGetValue(deviceId, out var session))
            return null;

        var device = await GetDeviceByIdAsync(deviceId, ct);

        session.IsRunning = false;
        session.IsCompleted = true;
        session.EndedAtDe = GermanTimeHelper.FormatFull();

        double currentCharge = device?.Battery?.ChargeLevelPercent ?? device?.BatteryLevelPercent ?? session.CurrentChargePercent;
        session.CurrentChargePercent = currentCharge;
        session.EndChargePercent = currentCharge;
        session.TotalDrainPercent = Math.Max(0.0, Math.Round(session.StartChargePercent - currentCharge, 1));

        var elapsed = DateTime.UtcNow - session.StartedAt;
        session.ElapsedSeconds = (int)elapsed.TotalSeconds;
        session.RemainingSeconds = 0;
        session.ElapsedDisplay = FormatTimeSpan(elapsed);
        session.RemainingDisplay = "00:00";
        session.ProgressPercent = 100.0;

        double hours = Math.Max(0.01, elapsed.TotalHours);
        session.DischargeRatePercentPerHour = Math.Round(session.TotalDrainPercent / hours, 1);
        if (session.DischargeRatePercentPerHour > 0.5 && currentCharge > 0)
        {
            session.EstimatedRuntimeHours = Math.Round(currentCharge / session.DischargeRatePercentPerHour, 1);
            session.EstimatedFullRuntimeHours = Math.Round(100.0 / session.DischargeRatePercentPerHour, 1);
        }

        // Evaluation verdict
        CalculateVerdict(session);

        session.LogEntries.Add($"[{GermanTimeHelper.FormatTimeOnly()}] - Test beendet. Endladung: {currentCharge:F1}% (Verlust: {session.TotalDrainPercent:F1}%, Rate: ~{session.DischargeRatePercentPerHour:F1}%/h)");

        SaveBatteryTests();

        // Stop remote stress test if active
        if (device != null && device.SourceType == "lhm-api" && !string.IsNullOrWhiteSpace(device.Host))
        {
            try
            {
                var client = _httpClientFactory.CreateClient("LhmClient");
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(2000);
                using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                await client.PostAsync($"http://{device.Host}:{device.Port}/api/control/stress/battery/stop", content, timeoutCts.Token);
                await client.PostAsync($"http://{device.Host}:{device.Port}/api/control/stress/stop-all", content, timeoutCts.Token);
            }
            catch { }
        }

        _recordingService.AddCustomProtocolEvent(
            deviceId,
            session.DeviceName,
            device?.OrderNumber,
            "BATTERIE",
            "Batterietest beendet",
            $"Batterietest abgeschlossen. Ergebnis: {session.Verdict} ({session.VerdictText}). Verlust: {session.TotalDrainPercent:F1}%.");

        return MapSessionToDto(session);
    }

    private void UpdateAndAttachBatteryTestState(HardwareDeviceReportDto dev)
    {
        // 1. Sync from remote DiagnoseTool if active
        if (dev.StressStatus != null && dev.StressStatus.IsBatteryTestRunning)
        {
            if (!_batteryTests.TryGetValue(dev.Id, out var existing) || !existing.IsRunning)
            {
                int duration = 30;
                var remoteSession = new BatteryTestSessionState
                {
                    DeviceId = dev.Id,
                    DeviceName = dev.Name,
                    IsRunning = true,
                    IsCompleted = false,
                    LoadMode = "youtube",
                    LoadModeDisplay = "YouTube 4K HDR Stress-Test",
                    DurationMinutes = duration,
                    StartedAt = DateTime.UtcNow.AddMinutes(-Math.Max(0, 30 - ParseMinutes(dev.StressStatus.BatteryTestRemaining))),
                    StartedAtDe = GermanTimeHelper.FormatFull(),
                    StartChargePercent = dev.StressStatus.BatteryStartPercent ?? dev.BatteryLevelPercent ?? 100.0,
                    CurrentChargePercent = dev.StressStatus.BatteryCurrentPercent ?? dev.BatteryLevelPercent ?? 100.0,
                    Verdict = "LAUFEND",
                    VerdictText = "Stresstest läuft via DiagnoseTool"
                };
                _batteryTests[dev.Id] = remoteSession;
            }
        }

        // 2. Local session update
        if (_batteryTests.TryGetValue(dev.Id, out var session))
        {
            if (session.IsRunning)
            {
                var elapsed = DateTime.UtcNow - session.StartedAt;
                session.ElapsedSeconds = (int)elapsed.TotalSeconds;
                int totalSec = session.DurationMinutes * 60;
                session.RemainingSeconds = Math.Max(0, totalSec - session.ElapsedSeconds);
                session.ElapsedDisplay = FormatTimeSpan(elapsed);
                session.RemainingDisplay = FormatTimeSpan(TimeSpan.FromSeconds(session.RemainingSeconds));
                session.ProgressPercent = Math.Clamp(Math.Round((double)session.ElapsedSeconds / totalSec * 100.0, 1), 0.0, 100.0);

                // Update current charge & metrics
                double currentCharge = dev.Battery?.ChargeLevelPercent ?? dev.BatteryLevelPercent ?? session.CurrentChargePercent;
                session.CurrentChargePercent = currentCharge;
                session.TotalDrainPercent = Math.Max(0.0, Math.Round(session.StartChargePercent - currentCharge, 1));

                double hours = Math.Max(0.01, elapsed.TotalHours);
                session.DischargeRatePercentPerHour = Math.Round(session.TotalDrainPercent / hours, 1);
                if (session.DischargeRatePercentPerHour > 0.5 && currentCharge > 0)
                {
                    session.EstimatedRuntimeHours = Math.Round(currentCharge / session.DischargeRatePercentPerHour, 1);
                    session.EstimatedFullRuntimeHours = Math.Round(100.0 / session.DischargeRatePercentPerHour, 1);
                }

                if (dev.Battery?.PowerWatts != null && dev.Battery.PowerWatts.Value > 0)
                {
                    session.MaxPowerWatts = Math.Max(session.MaxPowerWatts ?? 0, dev.Battery.PowerWatts.Value);
                    session.AvgPowerWatts = session.AvgPowerWatts == null ? dev.Battery.PowerWatts.Value : Math.Round((session.AvgPowerWatts.Value * 0.9) + (dev.Battery.PowerWatts.Value * 0.1), 1);
                }
                if (dev.Battery?.Voltage != null && dev.Battery.Voltage.Value > 0)
                {
                    session.MinVoltage = session.MinVoltage == null ? dev.Battery.Voltage.Value : Math.Min(session.MinVoltage.Value, dev.Battery.Voltage.Value);
                    session.AvgVoltage = session.AvgVoltage == null ? dev.Battery.Voltage.Value : Math.Round((session.AvgVoltage.Value * 0.9) + (dev.Battery.Voltage.Value * 0.1), 2);
                }
                if (dev.Battery?.Temperature != null && dev.Battery.Temperature.Value > 0)
                {
                    session.MaxTemp = Math.Max(session.MaxTemp ?? 0, dev.Battery.Temperature.Value);
                }

                // Check if completed
                if (session.RemainingSeconds <= 0)
                {
                    session.IsRunning = false;
                    session.IsCompleted = true;
                    session.EndedAtDe = GermanTimeHelper.FormatFull();
                    CalculateVerdict(session);
                    session.LogEntries.Add($"[{GermanTimeHelper.FormatTimeOnly()}] - Stresstest erfolgreich beendet! Endladung: {currentCharge:F1}% (-{session.TotalDrainPercent:F1}%)");
                    SaveBatteryTests();
                }
                else
                {
                    // Periodic log entry every 60s
                    int elapsedMin = (int)(elapsed.TotalSeconds / 60);
                    if (elapsedMin > 0 && session.LastLoggedMinute != elapsedMin)
                    {
                        session.LastLoggedMinute = elapsedMin;
                        session.LogEntries.Add($"[{GermanTimeHelper.FormatTimeOnly()}] - Verbleibend: {session.RemainingDisplay} | Ladestand: {currentCharge:F1}% (-{session.TotalDrainPercent:F1}%) | {dev.Battery?.PowerWatts ?? 0:F1} W");
                    }
                }
            }

            dev.BatteryTest = MapSessionToDto(session);
        }
        else if (dev.HasBattery)
        {
            // Default ready status
            dev.BatteryTest = new BatteryTestStatusDto
            {
                IsRunning = false,
                IsCompleted = false,
                DurationMinutes = 30,
                ElapsedDisplay = "00:00",
                RemainingDisplay = "30:00",
                StartChargePercent = dev.BatteryLevelPercent ?? 100.0,
                CurrentChargePercent = dev.BatteryLevelPercent ?? 100.0,
                HealthPercent = dev.Battery?.HealthPercent ?? 95.8,
                WearPercent = dev.Battery?.WearPercent ?? 4.2,
                Verdict = "BEREIT",
                VerdictText = "Bereit für 30-Minuten Batterie-Stresstest"
            };
        }
    }

    private (double? pct, double? watts) GetSimulatedDemoBatteryValues(string id)
    {
        if (_batteryTests.TryGetValue(id, out var session) && session.IsRunning)
        {
            double elapsedMin = (DateTime.UtcNow - session.StartedAt).TotalMinutes;
            double drainRatePerMin = session.LoadMode switch
            {
                "full" => 0.55,    // ~33% per hour
                "cpu" => 0.40,     // ~24% per hour
                "idle" => 0.12,    // ~7% per hour
                _ => 0.30          // ~18% per hour (YouTube 4K)
            };
            double simulatedDrain = elapsedMin * drainRatePerMin;
            double currentPct = Math.Max(5.0, Math.Round(session.StartChargePercent - simulatedDrain, 1));
            double currentWatts = session.LoadMode switch
            {
                "full" => 68.5,
                "cpu" => 48.0,
                "idle" => 12.5,
                _ => 36.8
            };
            return (currentPct, currentWatts);
        }
        return (null, null);
    }

    private async Task EnrichFromDiagnoseToolTelemetryAsync(HardwareDeviceReportDto dev, string host, int port, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var client = _httpClientFactory.CreateClient("LhmClient");
            using var resp = await client.GetAsync($"http://{host}:{port}/api/telemetry", linkedCts.Token);
            if (!resp.IsSuccessStatusCode) return;

            await using var s = await resp.Content.ReadAsStreamAsync(linkedCts.Token);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: linkedCts.Token);
            var root = doc.RootElement;

            if (root.TryGetProperty("system", out var sys))
            {
                if (sys.TryGetProperty("fullName", out var fn) && fn.ValueKind == JsonValueKind.String)
                    dev.SystemModel = fn.GetString();
                if (sys.TryGetProperty("serialNumber", out var sn) && sn.ValueKind == JsonValueKind.String)
                    dev.SerialNumber = sn.GetString();
            }

            if (root.TryGetProperty("battery", out var batt))
            {
                bool hasBattery = batt.TryGetProperty("hasBattery", out var hb) && hb.GetBoolean();
                if (hasBattery)
                {
                    dev.HasBattery = true;
                    dev.Battery ??= new BatteryDetailsDto();
                    dev.Battery.IsPresent = true;

                    if (batt.TryGetProperty("percent", out var bp) && bp.TryGetDouble(out double pVal))
                    {
                        dev.Battery.ChargeLevelPercent = pVal;
                        dev.BatteryLevelPercent = pVal;
                    }
                    if (batt.TryGetProperty("healthPercent", out var hp) && hp.TryGetDouble(out double hVal))
                    {
                        dev.Battery.HealthPercent = hVal;
                        dev.Battery.WearPercent = Math.Max(0.0, Math.Round(100.0 - hVal, 1));
                        dev.BatteryWearPercent = dev.Battery.WearPercent;
                    }
                    if (batt.TryGetProperty("designCapacity", out var dc) && dc.TryGetDouble(out double dcVal) && dcVal > 0)
                        dev.Battery.DesignCapacityMwh = dcVal;
                    if (batt.TryGetProperty("fullChargeCapacity", out var fc) && fc.TryGetDouble(out double fcVal) && fcVal > 0)
                        dev.Battery.FullChargeCapacityMwh = fcVal;
                    if (batt.TryGetProperty("voltage", out var bv) && bv.TryGetDouble(out double bvVal) && bvVal > 0)
                        dev.Battery.Voltage = bvVal;
                    if (batt.TryGetProperty("chargeRateWatts", out var cr) && cr.TryGetDouble(out double crVal))
                        dev.Battery.PowerWatts = crVal;
                    if (batt.TryGetProperty("isCharging", out var bic))
                        dev.Battery.IsCharging = bic.GetBoolean();
                    if (batt.TryGetProperty("isConnectedToAc", out var bac))
                        dev.Battery.IsConnectedToAc = bac.GetBoolean();
                    if (batt.TryGetProperty("remainingTimeDisplay", out var rtd) && rtd.ValueKind == JsonValueKind.String)
                        dev.Battery.RemainingTimeDisplay = rtd.GetString() ?? "--";
                    if (batt.TryGetProperty("chargingStatusDisplay", out var csd) && csd.ValueKind == JsonValueKind.String)
                        dev.Battery.ChargingStatusDisplay = csd.GetString() ?? "Bereit";
                    if (batt.TryGetProperty("manufacturer", out var bm) && bm.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(bm.GetString()))
                        dev.Battery.Manufacturer = bm.GetString()!;
                    if (batt.TryGetProperty("chemistry", out var bc) && bc.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(bc.GetString()))
                        dev.Battery.Chemistry = bc.GetString()!;
                }
            }
        }
        catch { }
    }

    private static void CalculateVerdict(BatteryTestSessionState session)
    {
        double health = session.HealthPercent ?? 100.0;
        double drain = session.TotalDrainPercent;

        if (health < 60.0 || (session.DurationMinutes >= 25 && drain >= 45.0))
        {
            session.Verdict = "KRITISCH";
            session.VerdictText = "Akkutausch dringend empfohlen (hoher Verschleiß oder rapide Entladung)";
        }
        else if (health < 78.0 || (session.DurationMinutes >= 25 && drain >= 30.0))
        {
            session.Verdict = "VERSCHLEISS ERHÖHT";
            session.VerdictText = "Akku zeigt spürbaren Kapazitätsverlust; Betrieb eingeschränkt";
        }
        else if (health < 88.0 || drain >= 22.0)
        {
            session.Verdict = "GUT";
            session.VerdictText = "Akkuzustand gut mit normaler altersüblicher Alterung";
        }
        else
        {
            session.Verdict = "OPTIMAL";
            session.VerdictText = "Hervorragender Akkuzustand und vorbildliche Entladestabilität";
        }
    }

    private static BatteryTestStatusDto MapSessionToDto(BatteryTestSessionState s)
    {
        return new BatteryTestStatusDto
        {
            IsRunning = s.IsRunning,
            IsCompleted = s.IsCompleted,
            LoadMode = s.LoadMode,
            LoadModeDisplay = s.LoadModeDisplay,
            DurationMinutes = s.DurationMinutes,
            StartedAtDe = s.StartedAtDe,
            EndedAtDe = s.EndedAtDe,
            ElapsedSeconds = s.ElapsedSeconds,
            RemainingSeconds = s.RemainingSeconds,
            ElapsedDisplay = s.ElapsedDisplay,
            RemainingDisplay = s.RemainingDisplay,
            ProgressPercent = s.ProgressPercent,
            StartChargePercent = s.StartChargePercent,
            CurrentChargePercent = s.CurrentChargePercent,
            EndChargePercent = s.EndChargePercent,
            TotalDrainPercent = s.TotalDrainPercent,
            DischargeRatePercentPerHour = s.DischargeRatePercentPerHour,
            EstimatedRuntimeHours = s.EstimatedRuntimeHours,
            EstimatedFullRuntimeHours = s.EstimatedFullRuntimeHours,
            AvgPowerWatts = s.AvgPowerWatts,
            MaxPowerWatts = s.MaxPowerWatts,
            AvgVoltage = s.AvgVoltage,
            MinVoltage = s.MinVoltage,
            MaxTemp = s.MaxTemp,
            HealthPercent = s.HealthPercent,
            WearPercent = s.WearPercent,
            ConnectedToAcWarning = s.ConnectedToAcWarning,
            Verdict = s.Verdict,
            VerdictText = s.VerdictText,
            LogEntries = new List<string>(s.LogEntries)
        };
    }

    private static string FormatTimeSpan(TimeSpan t)
    {
        int mins = (int)t.TotalMinutes;
        int secs = t.Seconds;
        return $"{mins:D2}:{secs:D2}";
    }

    private static int ParseMinutes(string? rem)
    {
        if (string.IsNullOrWhiteSpace(rem)) return 0;
        var parts = rem.Split(':');
        if (parts.Length > 0 && int.TryParse(parts[0], out int m)) return m;
        return 0;
    }

    private void LoadBatteryTests()
    {
        try
        {
            if (File.Exists(_batteryTestsPath))
            {
                string json = File.ReadAllText(_batteryTestsPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, BatteryTestSessionState>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _batteryTests[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch { }
    }

    private void SaveBatteryTests()
    {
        try
        {
            var dict = _batteryTests.ToDictionary(k => k.Key, v => v.Value);
            string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_batteryTestsPath, json);
        }
        catch { }
    }

    #endregion
}

public class BatteryTestSessionState
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public bool IsCompleted { get; set; }
    public string LoadMode { get; set; } = "youtube";
    public string LoadModeDisplay { get; set; } = "YouTube 4K HDR Stress-Test";
    public int DurationMinutes { get; set; } = 30;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public string StartedAtDe { get; set; } = string.Empty;
    public string? EndedAtDe { get; set; }
    public int ElapsedSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public string ElapsedDisplay { get; set; } = "00:00";
    public string RemainingDisplay { get; set; } = "30:00";
    public double ProgressPercent { get; set; }
    public double StartChargePercent { get; set; }
    public double CurrentChargePercent { get; set; }
    public double EndChargePercent { get; set; }
    public double TotalDrainPercent { get; set; }
    public double DischargeRatePercentPerHour { get; set; }
    public double? EstimatedRuntimeHours { get; set; }
    public double? EstimatedFullRuntimeHours { get; set; }
    public double? AvgPowerWatts { get; set; }
    public double? MaxPowerWatts { get; set; }
    public double? AvgVoltage { get; set; }
    public double? MinVoltage { get; set; }
    public double? MaxTemp { get; set; }
    public double? HealthPercent { get; set; }
    public double? WearPercent { get; set; }
    public bool ConnectedToAcWarning { get; set; }
    public string Verdict { get; set; } = "BEREIT";
    public string VerdictText { get; set; } = "";
    public int LastLoggedMinute { get; set; } = -1;
    public List<string> LogEntries { get; set; } = new();
}


