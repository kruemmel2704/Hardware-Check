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

builder.Services.AddSingleton<LocalLhmBridgeService>();
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

// Scan network for devices listening on Port 8085
app.MapPost("/api/hardware/scan", async (ScanNetworkRequest? req, LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    var scanResult = await monitorService.ScanNetworkForPort8085Async(req ?? new ScanNetworkRequest(), ct);
    return Results.Ok(scanResult);
});

// Toggle Demo Workstations
app.MapPost("/api/hardware/demo", async (DemoToggleRequest req, LhmDiscoveryAndMonitorService monitorService, CancellationToken ct) =>
{
    monitorService.DemoModeEnabled = req.Enabled;
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
public record RecordingToggleRequest(bool Enabled);
public record SimulateCrashRequest(string DeviceId, bool Offline);
