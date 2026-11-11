using System.Net;
using System.Net.Sockets;
using HardwareCheck.Api.Models;
using HardwareCheck.Api.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "wwwroot"
});

// Check if Port 8085 is free so we can listen on Port 8085 directly (unless external LHM GUI already uses 8085)
bool port8085Available = IsPortAvailable(8085);
bool port5085Available = IsPortAvailable(5085);

builder.WebHost.ConfigureKestrel(options =>
{
    if (port5085Available)
    {
        options.ListenAnyIP(5085);
    }
    if (port8085Available)
    {
        options.ListenAnyIP(8085);
    }
    if (!port5085Available && !port8085Available)
    {
        options.ListenAnyIP(5086);
    }
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("LhmClient", client =>
{
    client.Timeout = TimeSpan.FromMilliseconds(1500);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "CPM-HardwareCheck-CSharp/1.0");
});

// Längerer Timeout für die DiagnoseTool-Fernsteuerung (PDF-Erstellung, Tool-Start, UI-Aktionen)
builder.Services.AddHttpClient("DiagnoseToolRemote", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("User-Agent", "CPM-HardwareCheck-CSharp/1.0");
});

builder.Services.AddSingleton<LocalLhmBridgeService>();
builder.Services.AddSingleton<CrashReportStore>();
builder.Services.AddSingleton<HardwareRecordingService>();
builder.Services.AddSingleton<LhmDiscoveryAndMonitorService>(sp =>
{
    var svc = new LhmDiscoveryAndMonitorService(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<LocalLhmBridgeService>(),
        sp.GetRequiredService<HardwareRecordingService>(),
        sp.GetRequiredService<IWebHostEnvironment>());
    svc.SelfListeningOn8085 = port8085Available;
    return svc;
});
builder.Services.AddSingleton<UdpDiscoveryListenerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UdpDiscoveryListenerService>());
builder.Services.AddHostedService<RecordingBackgroundWorker>();

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Original Libre Hardware Monitor Web Server API endpoint (/data.json on Port 8085)
app.MapGet("/data.json", (LocalLhmBridgeService localBridge) =>
{
    var tree = localBridge.BuildLocalLhmTree();
    return Results.Json(tree);
});

// Dashboard summary & all available devices
app.MapGet("/api/hardware/dashboard", async (LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(data);
});

// Single device detail
app.MapGet("/api/hardware/devices/{id}", async (string id, LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    var dev = await monitorService.GetDeviceByIdAsync(id, ct);
    return dev != null ? Results.Ok(dev) : Results.NotFound(new { error = "Gerät nicht gefunden." });
});

// Add custom device IP:8085
app.MapPost("/api/hardware/devices", async (AddDeviceRequest req, LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Host))
    {
        return Results.BadRequest(new { error = "Bitte eine gültige IP-Adresse oder einen Hostnamen angeben." });
    }
    var created = monitorService.AddEndpoint(req);
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { endpoint = created, dashboard = data });
});

// Remove custom device
app.MapDelete("/api/hardware/devices/{id}", async (string id, LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    bool removed = monitorService.RemoveEndpoint(id);
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { removed, dashboard = data });
});

// Finish tests and archive device (exits DiagnoseTool on remote client and moves device to archive)
app.MapPost("/api/hardware/devices/{id}/finish-and-archive", async (
    string id,
    LhmDiscoveryAndMonitorService monitorService,
    CancellationToken ct) =>
{
    var archived = await monitorService.ArchiveEndpointAsync(id, ct);
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { success = true, archived, dashboard = data, message = "Tests abgeschlossen. Gerät archiviert und Client-Programm beendet." });
});

// Restore device from archive
app.MapPost("/api/hardware/devices/{id}/restore", async (
    string id,
    LhmDiscoveryAndMonitorService monitorService,
    CancellationToken ct) =>
{
    var restored = monitorService.RestoreEndpoint(id);
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { success = true, restored, dashboard = data, message = "Gerät aus dem Archiv wiederhergestellt." });
});

// Set order number for device and notify remote client
app.MapPost("/api/hardware/devices/{id}/order-number", async (
    string id,
    SetDeviceOrderNumberRequest req,
    LhmDiscoveryAndMonitorService monitorService,
    HardwareRecordingService recordingService,
    CancellationToken ct) =>
{
    var updated = await monitorService.UpdateEndpointOrderNumberAsync(id, req.OrderNumber, ct);
    if (updated != null)
    {
        recordingService.AddCustomProtocolEvent(
            id,
            updated.Name,
            updated.OrderNumber,
            "AUFTRAG",
            "Auftrag zugewiesen",
            $"Auftragsnummer '{req.OrderNumber}' wurde dem Gerät zugewiesen.");
    }
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { endpoint = updated, dashboard = data });
});

// Dismiss discovery alert for device
app.MapPost("/api/hardware/discovery/dismiss", async (
    DismissDiscoveryAlertRequest req,
    LhmDiscoveryAndMonitorService monitorService,
    CancellationToken ct) =>
{
    string target = req.DeviceId ?? req.AlertId ?? "";
    bool dismissed = !string.IsNullOrWhiteSpace(target) && monitorService.DismissDiscoveryAlert(target);
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { dismissed, dashboard = data });
});

// HTTP Discover endpoint: allows DiagnoseTool clients to announce themselves via HTTP (bridges Docker NAT/containers where UDP broadcast is blocked)
app.MapPost("/api/hardware/devices/discover", async (
    HttpDiscoverDeviceRequest req,
    HttpContext httpContext,
    LhmDiscoveryAndMonitorService monitorService,
    CancellationToken ct) =>
{
    string clientIp = req.Host?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(clientIp) || clientIp == "127.0.0.1" || clientIp == "localhost")
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp != null)
        {
            clientIp = remoteIp.ToString();
            if (clientIp.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            {
                clientIp = clientIp.Substring(7);
            }
        }
    }

    if (string.IsNullOrWhiteSpace(clientIp))
    {
        clientIp = "127.0.0.1";
    }

    int clientPort = req.Port > 0 ? req.Port : 8085;
    string machineName = !string.IsNullOrWhiteSpace(req.MachineName) ? req.MachineName.Trim() : "CPM-Gerät";
    string displayName = !string.IsNullOrWhiteSpace(req.SystemModel)
        ? $"{machineName} ({req.SystemModel})"
        : machineName;

    var (endpoint, isNew) = monitorService.RegisterDiscoveredEndpoint(
        clientIp,
        clientPort,
        displayName,
        string.IsNullOrWhiteSpace(req.OrderNumber) ? null : req.OrderNumber,
        req.SystemModel);

    return Results.Ok(new
    {
        success = true,
        type = "HARDWARE_CHECK_ACK",
        message = "Connected to CPM Hardware-Check Server (via HTTP Discover)",
        deviceId = endpoint.Id,
        serverHost = LhmDiscoveryAndMonitorService.GetPrimaryHostIp(),
        serverPort = 5085,
        clientHost = clientIp,
        clientPort = clientPort,
        isNew = isNew,
        orderNumber = endpoint.OrderNumber
    });
});

// Scan network for devices listening on Port 8085 + UDP Discovery Ping
app.MapPost("/api/hardware/scan", async (
    ScanNetworkRequest? req,
    LhmDiscoveryAndMonitorService monitorService,
    UdpDiscoveryListenerService udpListener,
    CancellationToken ct) =>
{
    _ = udpListener.BroadcastPingAsync(ct);
    var scanResult = await monitorService.ScanNetworkForPort8085Async(req ?? new ScanNetworkRequest(), ct);
    return Results.Ok(scanResult);
});

// Remote Control Proxy: Start/Stop Stress tests (CPU, RAM, GPU/FurMark), Stop All, Recording
app.MapPost("/api/hardware/devices/{id}/control/{*action}", async (
    string id,
    string action,
    HttpRequest request,
    LhmDiscoveryAndMonitorService monitorService,
    HardwareRecordingService recordingService,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var target = DiagnoseToolRemoteProxy.ResolveTarget(id, monitorService);
    if (target == null)
    {
        return Results.NotFound(new { error = $"Gerät '{id}' wurde nicht gefunden oder ist ein Demo-Gerät." });
    }
    string targetHost = target.Value.Host;
    int targetPort = target.Value.Port;

    string body = "";
    using (var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8))
    {
        body = await reader.ReadToEndAsync(ct);
    }

    string cleanAction = action.TrimStart('/');
    var client = httpClientFactory.CreateClient("LhmClient");

    // Orchestration: Volllast (CPU-Stresstest + FurMark GPU Donut gleichzeitig)
    if (cleanAction == "stress/full/start")
    {
        int durationMinutes = 60;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("durationMinutes", out var dm) && dm.TryGetInt32(out int dVal) && dVal > 0)
                {
                    durationMinutes = dVal;
                }
            }
        }
        catch { }

        try
        {
            // 1. DiagnoseTool /api/control/stress/full/start direkt aufrufen (falls unterstützt)
            var fullPayload = System.Text.Json.JsonSerializer.Serialize(new { durationMinutes });
            using var fullContent = new StringContent(fullPayload, System.Text.Encoding.UTF8, "application/json");
            var fullResp = await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/full/start", fullContent, ct);
            if (fullResp.IsSuccessStatusCode)
            {
                return Results.Ok(new
                {
                    success = true,
                    action = "full_start",
                    durationMinutes,
                    message = $"Volllast-Test (CPU + FurMark GPU) für {durationMinutes} Minuten gestartet."
                });
            }

            // Fallback: Einzelkomponenten starten
            // 1. FurMark GPU Donut mit Laufzeit starten
            using var gpuStartContent = new StringContent(fullPayload, System.Text.Encoding.UTF8, "application/json");
            _ = await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/gpu/donut", gpuStartContent, ct);

            // 2. CPU Stresstest mit Laufzeit starten
            using var cpuStartContent = new StringContent(fullPayload, System.Text.Encoding.UTF8, "application/json");
            _ = await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/cpu/start", cpuStartContent, ct);

            // 3. FurMark Auto-Stopp nach Ablauf der Dauer planen
            if (durationMinutes > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(durationMinutes));
                        using var gpuStopContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                        await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/gpu/stop", gpuStopContent);
                    }
                    catch { }
                });
            }

            return Results.Ok(new
            {
                success = true,
                action = "full_start",
                durationMinutes,
                message = $"Volllast-Test (CPU + FurMark GPU) für {durationMinutes} Minuten gestartet."
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"Volllast-Test konnte nicht gestartet werden ({targetHost}:{targetPort}): {ex.Message}" }, statusCode: 502);
        }
    }

    if (cleanAction == "stress/full/stop")
    {
        try
        {
            using var s1 = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var s2 = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            _ = await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/gpu/stop", s1, ct);
            _ = await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/cpu/stop", s2, ct);
            return Results.Ok(new { success = true, action = "full_stop", message = "Volllast-Test (CPU & GPU) gestoppt." });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"Volllast-Test konnte nicht gestoppt werden: {ex.Message}" }, statusCode: 502);
        }
    }

    // FurMark Donut mit optionaler automatischer Stopp-Dauer
    if (cleanAction == "stress/gpu/donut")
    {
        int durationMinutes = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("durationMinutes", out var dm) && dm.TryGetInt32(out int dVal) && dVal > 0)
                {
                    durationMinutes = dVal;
                }
            }
        }
        catch { }

        try
        {
            using var donutContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var resp = await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/gpu/donut", donutContent, ct);
            string responseBody = await resp.Content.ReadAsStringAsync(ct);

            if (durationMinutes > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(durationMinutes));
                        using var gpuStopContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                        await client.PostAsync($"http://{targetHost}:{targetPort}/api/control/stress/gpu/stop", gpuStopContent);
                    }
                    catch { }
                });
            }

            return Results.Content(responseBody, "application/json; charset=utf-8", statusCode: (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"FurMark Donut konnte nicht gestartet werden: {ex.Message}" }, statusCode: 502);
        }
    }

    if (cleanAction == "recording/start")
    {
        recordingService.SetRecordingState(true);
    }

    string targetUrl = $"http://{targetHost}:{targetPort}/api/control/{cleanAction}";
    try
    {
        var remoteClient = httpClientFactory.CreateClient("DiagnoseToolRemote");
        using var content = new StringContent(string.IsNullOrWhiteSpace(body) ? "{}" : body, System.Text.Encoding.UTF8, "application/json");
        using var resp = await remoteClient.PostAsync(targetUrl, content, ct);

        string responseBody = await resp.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json; charset=utf-8", statusCode: (int)resp.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Verbindung zum DiagnoseTool fehlgeschlagen ({targetUrl}): {ex.Message}" }, statusCode: 502);
    }
});

// ==============================================================================
// DiagnoseTool Fernsteuerung (voller Funktionsumfang via REST)
// ------------------------------------------------------------------------------
// GET  /api/hardware/devices/{id}/remote               -> Endpunkt-Katalog des DiagnoseTools
// GET  /api/hardware/devices/{id}/remote/{*path}       -> GET  http://<client>:8085/api/{path}
// POST /api/hardware/devices/{id}/remote/{*path}       -> POST http://<client>:8085/api/{path}
// Beispiele: remote/app/state, remote/tools, remote/stress/cpu, remote/battery-test,
//            remote/recording?samples=true, remote/report/system (PDF),
//            remote/control/tools/OCCT/launch, remote/control/repair/sfc, remote/control/ui/tab
// ==============================================================================
app.MapGet("/api/hardware/devices/{id}/remote", (
    string id,
    HttpContext httpContext,
    LhmDiscoveryAndMonitorService monitorService,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
    DiagnoseToolRemoteProxy.ForwardAsync(id, "", httpContext, monitorService, httpClientFactory, ct));

app.MapMethods("/api/hardware/devices/{id}/remote/{*path}", new[] { "GET", "POST" }, (
    string id,
    string? path,
    HttpContext httpContext,
    LhmDiscoveryAndMonitorService monitorService,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
    DiagnoseToolRemoteProxy.ForwardAsync(id, path ?? "", httpContext, monitorService, httpClientFactory, ct));

// Kompakter Gesamtzustand eines DiagnoseTool-Clients (App, Tools, Stresstests, Akkutest, Aufzeichnung)
app.MapGet("/api/hardware/devices/{id}/remote-overview", async (
    string id,
    LhmDiscoveryAndMonitorService monitorService,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var target = DiagnoseToolRemoteProxy.ResolveTarget(id, monitorService);
    if (target == null)
    {
        return Results.NotFound(new { error = $"Gerät '{id}' wurde nicht gefunden oder ist ein Demo-Gerät." });
    }

    var client = httpClientFactory.CreateClient("DiagnoseToolRemote");
    string baseUrl = $"http://{target.Value.Host}:{target.Value.Port}/api";
    string[] parts = { "app/state", "tools", "repair", "hwinfo", "stress/cpu", "stress/ram", "stress/gpu", "battery-test", "recording" };

    var tasks = parts.ToDictionary(p => p, p => DiagnoseToolRemoteProxy.TryGetJsonAsync(client, $"{baseUrl}/{p}", ct));
    await Task.WhenAll(tasks.Values);

    bool online = tasks.Values.Any(t => t.Result != null);
    return Results.Ok(new
    {
        deviceId = id,
        host = target.Value.Host,
        port = target.Value.Port,
        online,
        app = tasks["app/state"].Result,
        tools = tasks["tools"].Result,
        repair = tasks["repair"].Result,
        hwinfo = tasks["hwinfo"].Result,
        cpuStress = tasks["stress/cpu"].Result,
        ramStress = tasks["stress/ram"].Result,
        gpuStress = tasks["stress/gpu"].Result,
        batteryTest = tasks["battery-test"].Result,
        recording = tasks["recording"].Result
    });
});

// ==============================================================================
// Programm-Lebenszyklus & Absturzanalyse (Windows-Ereignisanzeige der Clients)
// ==============================================================================

// Ordnungsgemäßes Beenden des DiagnoseTools -> "Programm geschlossen" statt "Absturz"
app.MapPost("/api/hardware/devices/goodbye", (
    ClientLifecycleRequest req,
    HttpContext httpContext,
    LhmDiscoveryAndMonitorService monitorService,
    HardwareRecordingService recordingService) =>
{
    var endpoints = ClientLifecycleHelper.ResolveEndpoints(req.Identity, httpContext, monitorService, registerIfMissing: false);
    if (endpoints.Count == 0)
    {
        return Results.NotFound(new { success = false, error = $"Kein Gerät für {req.Identity?.Host}:{req.Identity?.Port} registriert." });
    }

    foreach (var endpoint in endpoints)
    {
        recordingService.MarkGracefulClose(
            endpoint.Id,
            endpoint.Name,
            endpoint.OrderNumber ?? req.Identity?.OrderNumber,
            $"{endpoint.Host}:{endpoint.Port}",
            req.Reason,
            req.Identity?.SessionId,
            req.UptimeSeconds);
    }

    return Results.Ok(new { success = true, deviceIds = endpoints.Select(e => e.Id), state = "closed", message = "Programm geschlossen protokolliert." });
});

// Absturzbericht vom DiagnoseTool (wird nach dem Neustart des Clients automatisch gesendet)
app.MapPost("/api/hardware/devices/crash-report", (
    ClientCrashReportRequest req,
    HttpContext httpContext,
    LhmDiscoveryAndMonitorService monitorService,
    HardwareRecordingService recordingService,
    CrashReportStore crashStore) =>
{
    if (req.Report.ValueKind != System.Text.Json.JsonValueKind.Object)
    {
        return Results.BadRequest(new { success = false, error = "Kein Absturzbericht übermittelt." });
    }

    var endpoints = ClientLifecycleHelper.ResolveEndpoints(req.Identity, httpContext, monitorService, registerIfMissing: true);
    if (endpoints.Count == 0)
    {
        return Results.BadRequest(new { success = false, error = "Gerät konnte nicht zugeordnet werden." });
    }

    var primary = endpoints[0];
    string reportId = req.Report.TryGetProperty("reportId", out var rid) ? rid.GetString() ?? "" : "";
    bool duplicate = !string.IsNullOrWhiteSpace(reportId) && crashStore.Contains(reportId);
    var summary = crashStore.Add(primary.Id, primary.Name, primary.OrderNumber ?? req.Identity?.OrderNumber, req.Report);
    if (!duplicate)
    {
        foreach (var endpoint in endpoints)
        {
            recordingService.ApplyCrashReport(endpoint.Id, endpoint.Name, endpoint.OrderNumber, $"{endpoint.Host}:{endpoint.Port}", summary, addAnalysisEvent: endpoint == primary);
        }
    }

    return Results.Ok(new { success = true, duplicate, deviceIds = endpoints.Select(e => e.Id), summary });
});

// Alle Absturzberichte (optional gefiltert nach Gerät)
app.MapGet("/api/hardware/crash-reports", (string? deviceId, CrashReportStore crashStore) =>
    Results.Ok(crashStore.GetSummaries(deviceId)));

app.MapGet("/api/hardware/devices/{id}/crash-reports", (string id, CrashReportStore crashStore) =>
    Results.Ok(crashStore.GetSummaries(id)));

// Vollständiger Absturzbericht inkl. Befunde und komplettem Ereignisanzeige-Auszug
app.MapGet("/api/hardware/crash-reports/{reportId}", (string reportId, CrashReportStore crashStore) =>
{
    var entry = crashStore.Get(reportId);
    if (entry == null) return Results.NotFound(new { error = $"Absturzbericht '{reportId}' nicht gefunden." });
    return Results.Ok(new { summary = entry.Value.Summary, report = entry.Value.Report });
});

// Absturzbericht als Textdatei
app.MapGet("/api/hardware/crash-reports/{reportId}/export", (string reportId, CrashReportStore crashStore) =>
{
    var entry = crashStore.Get(reportId);
    if (entry == null) return Results.NotFound(new { error = $"Absturzbericht '{reportId}' nicht gefunden." });
    string text = CrashReportStore.BuildText(entry.Value.Summary, entry.Value.Report);
    byte[] bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray();
    string safeName = string.Join("_", entry.Value.Summary.DeviceName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
    return Results.File(bytes, "text/plain; charset=utf-8", $"CPM_Absturzanalyse_{safeName}_{entry.Value.Summary.ReportId}.txt");
});

app.MapDelete("/api/hardware/crash-reports/{reportId}", (string reportId, CrashReportStore crashStore) =>
    Results.Ok(new { removed = crashStore.Delete(reportId) }));

// Ereignisanzeige eines Clients jetzt analysieren (letzte N Stunden) bzw. letzten Absturzbericht abholen
app.MapPost("/api/hardware/devices/{id}/crash-analysis/pull", async (
    string id,
    int? hours,
    bool? lastCrash,
    LhmDiscoveryAndMonitorService monitorService,
    HardwareRecordingService recordingService,
    CrashReportStore crashStore,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var target = DiagnoseToolRemoteProxy.ResolveTarget(id, monitorService);
    var ep = monitorService.GetEndpointById(id);
    if (target == null || ep == null)
    {
        return Results.NotFound(new { error = $"Gerät '{id}' wurde nicht gefunden oder ist ein Demo-Gerät." });
    }

    var client = httpClientFactory.CreateClient("DiagnoseToolRemote");
    string url = lastCrash == true
        ? $"http://{target.Value.Host}:{target.Value.Port}/api/crash/last"
        : $"http://{target.Value.Host}:{target.Value.Port}/api/eventlog/analysis?hours={Math.Clamp(hours ?? 24, 1, 720)}";

    try
    {
        using var resp = await client.GetAsync(url, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            return Results.Content(body, "application/json; charset=utf-8", statusCode: (int)resp.StatusCode);
        }

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var report = doc.RootElement.Clone();
        string reportId = report.TryGetProperty("reportId", out var rid) ? rid.GetString() ?? "" : "";
        bool duplicate = !string.IsNullOrWhiteSpace(reportId) && crashStore.Contains(reportId);
        var summary = crashStore.Add(ep.Id, ep.Name, ep.OrderNumber, report);
        if (!duplicate)
        {
            recordingService.ApplyCrashReport(ep.Id, ep.Name, ep.OrderNumber, $"{ep.Host}:{ep.Port}", summary);
        }
        return Results.Ok(new { success = true, duplicate, summary });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Ereignisanzeige konnte nicht abgerufen werden ({url}): {ex.Message}" }, statusCode: 502);
    }
});

// Toggle Demo Workstations
app.MapPost("/api/hardware/demo", async (DemoToggleRequest req, LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    monitorService.DemoModeEnabled = req.Enabled;
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(data);
});

// Toggle Grundgerät / Server selbst in der Auflistung
app.MapPost("/api/hardware/local-device/toggle", async (LocalDeviceToggleRequest req, LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    monitorService.IncludeLocalDevice = req.Enabled;
    var data = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(data);
});

// Network & Listener Info
app.MapGet("/api/hardware/network-info", (LhmDiscoveryAndMonitorService monitorService, HardwareRecordingService recordingService) =>
{
    return Results.Ok(new
    {
        detectedSubnet = LhmDiscoveryAndMonitorService.DetectLocalSubnetPrefix() ?? "192.168.178",
        defaultPort = 8085,
        selfListeningOn8085 = monitorService.SelfListeningOn8085,
        demoModeEnabled = monitorService.DemoModeEnabled,
        includeLocalDevice = monitorService.IncludeLocalDevice,
        currentTimeDe = GermanTimeHelper.FormatFull(),
        isRecording = recordingService.IsRecording
    });
});

// Aufzeichnung & Absturz-Protokoll Status abrufen
app.MapGet("/api/hardware/recording", (HardwareRecordingService recordingService) =>
{
    return Results.Ok(recordingService.GetStatus());
});

// Aufzeichnung Starten / Pausieren
app.MapPost("/api/hardware/recording/toggle", (RecordingToggleRequest req, HardwareRecordingService recordingService) =>
{
    recordingService.SetRecordingState(req.Enabled);
    return Results.Ok(recordingService.GetStatus());
});

// Aufzeichnung & Absturz-Protokoll leeren
app.MapPost("/api/hardware/recording/clear", (HardwareRecordingService recordingService) =>
{
    recordingService.ClearProtocolAndSamples();
    return Results.Ok(recordingService.GetStatus());
});

// Verbindungsabbruch ("Absturz") für ein Gerät simulieren/testen oder Verbindung wiederherstellen
app.MapPost("/api/hardware/recording/simulate-crash", async (
    SimulateCrashRequest req,
    HardwareRecordingService recordingService,
    LhmDiscoveryAndMonitorService monitorService,
    CancellationToken ct) =>
{
    recordingService.ToggleSimulatedCrash(req.DeviceId, req.Offline);
    var dashboard = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new
    {
        recording = recordingService.GetStatus(),
        dashboard
    });
});

// Protokoll als .log Datei herunterladen (Deutsche Zeitzone)
app.MapGet("/api/hardware/recording/export", (HardwareRecordingService recordingService) =>
{
    string content = recordingService.BuildLogFileContent();
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
    string fileName = $"CPM_Absturz_Protokoll_{GermanTimeHelper.GetGermanNow():yyyy-MM-dd_HH-mm}.log";
    return Results.File(bytes, "text/plain; charset=utf-8", fileName);
});

// Offizieller System-Prüfbericht als PDF herunterladen (exakt wie im DiagnoseTool)
app.MapGet("/api/hardware/devices/{id}/report/pdf", async (
    string id,
    LhmDiscoveryAndMonitorService monitorService,
    HardwareRecordingService recordingService,
    IWebHostEnvironment env,
    CancellationToken ct) =>
{
    var dev = await monitorService.GetDeviceByIdAsync(id, ct);
    if (dev == null)
    {
        return Results.NotFound(new { error = $"Gerät mit ID '{id}' nicht gefunden." });
    }

    var samples = recordingService.GetSamplesForDeviceChronological(id);
    string logoPath = Path.Combine(env.WebRootPath ?? "wwwroot", "cpm-logo-normal__4_.png");

    byte[] pdfBytes = PdfReportGenerator.GenerateSystemReport(dev, samples, logoPath);
    string orderNo = !string.IsNullOrWhiteSpace(dev.OrderNumber) ? $"_Auftrag_{dev.OrderNumber}" : "";
    string safeName = string.Join("_", dev.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
    string fileName = $"CPM_System_Pruefbericht{orderNo}_{safeName}_{DateTime.Now:yyyy-MM-dd}.pdf";

    return Results.File(pdfBytes, "application/pdf", fileName);
});

// Batterie-Stresstest starten (z.B. 30 Min. YouTube, CPU oder Volllast)
app.MapPost("/api/hardware/devices/{id}/battery-test/start", async (
    string id,
    StartBatteryTestRequest req,
    LhmDiscoveryAndMonitorService monitorService,
    CancellationToken ct) =>
{
    var status = await monitorService.StartBatteryTestAsync(id, req, ct);
    if (status == null)
    {
        return Results.NotFound(new { error = $"Gerät '{id}' nicht gefunden." });
    }
    var dashboard = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { success = true, batteryTest = status, dashboard });
});

// Batterie-Stresstest stoppen
app.MapPost("/api/hardware/devices/{id}/battery-test/stop", async (
    string id,
    LhmDiscoveryAndMonitorService monitorService,
    CancellationToken ct) =>
{
    var status = await monitorService.StopBatteryTestAsync(id, ct);
    if (status == null)
    {
        return Results.NotFound(new { error = $"Kein laufender Batterietest für '{id}' gefunden." });
    }
    var dashboard = await monitorService.GetDashboardDataAsync(ct);
    return Results.Ok(new { success = true, batteryTest = status, dashboard });
});

// Offizieller Batterie-Prüfbericht als PDF herunterladen (exakt wie im DiagnoseTool)
app.MapGet("/api/hardware/devices/{id}/report/battery-pdf", async (
    string id,
    LhmDiscoveryAndMonitorService monitorService,
    HardwareRecordingService recordingService,
    IWebHostEnvironment env,
    CancellationToken ct) =>
{
    var dev = await monitorService.GetDeviceByIdAsync(id, ct);
    if (dev == null)
    {
        return Results.NotFound(new { error = $"Gerät mit ID '{id}' nicht gefunden." });
    }

    var samples = recordingService.GetSamplesForDeviceChronological(id);
    string logoPath = Path.Combine(env.WebRootPath ?? "wwwroot", "cpm-logo-normal__4_.png");

    byte[] pdfBytes = PdfReportGenerator.GenerateBatteryReport(dev, samples, logoPath);
    string orderNo = !string.IsNullOrWhiteSpace(dev.OrderNumber) ? $"_Auftrag_{dev.OrderNumber}" : "";
    string safeName = string.Join("_", dev.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
    string fileName = $"CPM_Batterie_Pruefbericht{orderNo}_{safeName}_{DateTime.Now:yyyy-MM-dd}.pdf";

    return Results.File(pdfBytes, "application/pdf", fileName);
});

app.MapFallbackToFile("index.html");

app.Run();

static bool IsPortAvailable(int port)
{
    try
    {
        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch
    {
        return false;
    }
}

public record DemoToggleRequest(bool Enabled);

public class ClientIdentityDto
{
    public string? Host { get; set; }
    public int Port { get; set; } = 8085;
    public string? MachineName { get; set; }
    public string? OrderNumber { get; set; }
    public string? SessionId { get; set; }
    public string? AppVersion { get; set; }
}

public class ClientLifecycleRequest
{
    public ClientIdentityDto? Identity { get; set; }
    public string? Reason { get; set; }
    public string? ClosedAtUtc { get; set; }
    public int? UptimeSeconds { get; set; }
}

public class ClientCrashReportRequest
{
    public ClientIdentityDto? Identity { get; set; }
    public System.Text.Json.JsonElement Report { get; set; }
}

public static class ClientLifecycleHelper
{
    private static string RemoteIp(HttpContext httpContext)
    {
        string remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        return remoteIp.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) ? remoteIp[7..] : remoteIp;
    }

    /// <summary>
    /// Ermittelt alle Geräte-Einträge eines Clients (ein Rechner kann z.B. unter 127.0.0.1 und seiner LAN-IP registriert sein).
    /// </summary>
    public static List<SavedEndpointConfig> ResolveEndpoints(
        ClientIdentityDto? identity,
        HttpContext httpContext,
        LhmDiscoveryAndMonitorService monitorService,
        bool registerIfMissing)
    {
        string host = identity?.Host?.Trim() ?? "";
        int port = identity?.Port > 0 ? identity.Port : 8085;
        string remoteIp = RemoteIp(httpContext);

        var hosts = new List<string?> { host, remoteIp };
        bool isLoopback = remoteIp is "127.0.0.1" or "::1" || host is "127.0.0.1" or "localhost";
        if (isLoopback) hosts.AddRange(new[] { "127.0.0.1", "localhost" });

        var endpoints = monitorService.FindEndpoints(hosts, port);

        if (endpoints.Count == 0)
        {
            var byName = monitorService.FindEndpoint(null, port, identity?.MachineName);
            if (byName != null) endpoints.Add(byName);
        }

        if (endpoints.Count == 0 && registerIfMissing)
        {
            string useHost = !string.IsNullOrWhiteSpace(host) && host != "127.0.0.1" ? host : (string.IsNullOrWhiteSpace(remoteIp) ? "127.0.0.1" : remoteIp);
            endpoints.Add(monitorService.RegisterDiscoveredEndpoint(useHost, port, identity?.MachineName ?? "CPM-Gerät", identity?.OrderNumber, null).endpoint);
        }

        return endpoints;
    }
}
public record LocalDeviceToggleRequest(bool Enabled);
public record RecordingToggleRequest(bool Enabled);
public record SimulateCrashRequest(string DeviceId, bool Offline);
