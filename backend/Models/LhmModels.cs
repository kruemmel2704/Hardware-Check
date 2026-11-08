using System.Text.Json.Serialization;

namespace HardwareCheck.Api.Models;

/// <summary>
/// Repräsentiert einen Knoten im originalen Libre Hardware Monitor /data.json Baum.
/// </summary>
public class LhmNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("Text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("Min")]
    public string Min { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("Max")]
    public string Max { get; set; } = string.Empty;

    [JsonPropertyName("SensorId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SensorId { get; set; }

    [JsonPropertyName("Type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("ImageURL")]
    public string ImageURL { get; set; } = string.Empty;

    [JsonPropertyName("Children")]
    public List<LhmNode> Children { get; set; } = new();
}

public class SavedEndpointConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8085;
    public bool IsEnabled { get; set; } = true;
    public bool IsAutoDiscovered { get; set; } = false;
    public string? OrderNumber { get; set; }
    public string? TestType { get; set; } // "hardware-test" | "battery-test" | "combined"
}

public class GpuQuickMetricDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty; // "NVIDIA" | "AMD" | "Intel" | "Apple" | "Sonstige"
    public string TypeLabel { get; set; } = "Dediziert"; // "Dedizierte GPU" | "Integrierte GPU (APU)"
    public bool IsDedicated { get; set; }
    public double? Temp { get; set; }
    public double? HotSpotTemp { get; set; }
    public double? Load { get; set; }
    public double? PowerWatts { get; set; }
    public double? ClockMhz { get; set; }
    public double? VramUsedGb { get; set; }
    public double? VramTotalGb { get; set; }
    public double? FanRpm { get; set; }
    public string Status { get; set; } = "optimal"; // optimal | warning | critical
}

public class QuickMetricsDto
{
    public string CpuName { get; set; } = "Unbekannter Prozessor";
    public double? CpuTemp { get; set; }
    public double? CpuMaxCoreTemp { get; set; }
    public double? CpuLoad { get; set; }
    public double? CpuClockMhz { get; set; }
    public double? CpuPowerWatts { get; set; }
    public double? CpuVoltage { get; set; }
    public double? CpuFanRpm { get; set; }

    public int GpuCount { get; set; }
    public List<GpuQuickMetricDto> Gpus { get; set; } = new();

    public string GpuName { get; set; } = "Integrierte / Keine GPU";
    public double? GpuTemp { get; set; }
    public double? GpuHotSpotTemp { get; set; }
    public double? GpuLoad { get; set; }
    public double? GpuPowerWatts { get; set; }
    public double? GpuClockMhz { get; set; }
    public double? GpuVramUsedGb { get; set; }
    public double? GpuVramTotalGb { get; set; }
    public double? GpuFanRpm { get; set; }

    public double? RamUsedGb { get; set; }
    public double? RamAvailableGb { get; set; }
    public double? RamTotalGb { get; set; }
    public double? RamLoadPercent { get; set; }

    public string MotherboardName { get; set; } = "System Mainboard";
    public double? MainboardTemp { get; set; }
    public double? VrmTemp { get; set; }

    public string StorageName { get; set; } = "Systemlaufwerk";
    public double? StorageTemp { get; set; }
    public double? StorageUsedPercent { get; set; }
    public double? StorageLifePercent { get; set; }

    public int StorageCount { get; set; }
    public int FanCount { get; set; }
    public int NetworkCount { get; set; }
    public int WarningCount { get; set; }
    public int CriticalCount { get; set; }
}

public class SensorItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SensorType { get; set; } = string.Empty;
    public string CategoryDe { get; set; } = string.Empty;
    public string ValueFormatted { get; set; } = string.Empty;
    public string MinFormatted { get; set; } = string.Empty;
    public string MaxFormatted { get; set; } = string.Empty;
    public double? ValueNumeric { get; set; }
    public double? MinNumeric { get; set; }
    public double? MaxNumeric { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Status { get; set; } = "optimal"; // optimal | warning | critical
}

public class HardwareComponentDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Other"; // CPU | GPU | Mainboard | RAM | Storage | Network | Controller | Battery | Other
    public string CategoryDe { get; set; } = "Sonstiges";
    public string ImageUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "optimal";
    public double? PrimaryTemp { get; set; }
    public double? PrimaryLoad { get; set; }
    public string? PrimaryMetricLabel { get; set; }
    public string? PrimaryMetricValue { get; set; }
    public List<SensorItemDto> Sensors { get; set; } = new();
    public List<HardwareComponentDto> SubHardware { get; set; } = new();
}

public class HardwareDeviceReportDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8085;
    public string EndpointUrl { get; set; } = string.Empty;
    public string SourceType { get; set; } = "lhm-api"; // lhm-api | local-bridge | demo
    public string SourceBadge { get; set; } = "Port 8085 API";
    public bool IsOnline { get; set; }
    public long LatencyMs { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
    public string? OrderNumber { get; set; }
    public string? TestType { get; set; } // "hardware-test" | "battery-test" | "combined"
    public bool HasBattery { get; set; }
    public double? BatteryLevelPercent { get; set; }
    public double? BatteryWearPercent { get; set; }
    public int TotalHardwareCount { get; set; }
    public int TotalSensorCount { get; set; }
    public DeviceStressStatusDto? StressStatus { get; set; }
    public QuickMetricsDto Summary { get; set; } = new();
    public List<HardwareComponentDto> Components { get; set; } = new();
    public LhmNode? RawTree { get; set; }
}

public class DeviceStressStatusDto
{
    public bool IsCpuStressRunning { get; set; }
    public string CpuStressRemaining { get; set; } = "";
    public int CpuStressThreads { get; set; }
    public bool IsRamStressRunning { get; set; }
    public string RamStressRemaining { get; set; } = "";
    public double RamStressAllocatedGb { get; set; }
    public int RamStressPassCount { get; set; }
    public int RamStressErrorCount { get; set; }
    public bool IsGpuStressRunning { get; set; }
    public bool IsFurMarkFound { get; set; }
    public bool IsRecordingRunning { get; set; }
    public string RecordingDuration { get; set; } = "";
    public int RecordedSamplesCount { get; set; }
}

public class DashboardResponseDto
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string TimestampDe { get; set; } = string.Empty;
    public int ListenerPort { get; set; } = 8085;
    public bool LocalPort8085Active { get; set; }
    public bool DemoModeEnabled { get; set; }
    public bool IsRecording { get; set; } = true;
    public int TotalCrashesLogged { get; set; }
    public int OnlineDevicesCount { get; set; }
    public int TotalHardwareComponentsCount { get; set; }
    public int TotalSensorsCount { get; set; }
    public List<HardwareDeviceReportDto> Devices { get; set; } = new();
    public List<DeviceDiscoveryAlert> DiscoveryAlerts { get; set; } = new();
}

public class TelemetrySnapshotPointDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string TimestampDe { get; set; } = string.Empty; // z.B. "06.11.2026 12:35:10"
    public string TimeShortDe { get; set; } = string.Empty; // z.B. "12:35:10"
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string? OrderNumber { get; set; }
    public string? TestType { get; set; }
    public string HostPort { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public string StatusText { get; set; } = "Online"; // "Online" oder "Absturz / Verbindung verloren"
    public double? BatteryLevelPercent { get; set; }

    // CPU Parameter
    public double? CpuTemp { get; set; }
    public double? CpuMaxCoreTemp { get; set; }
    public double? CpuLoad { get; set; }
    public double? CpuClockMhz { get; set; }
    public double? CpuPowerWatts { get; set; }
    public double? CpuVoltage { get; set; }
    public double? CpuFanRpm { get; set; }

    // GPU Parameter
    public int GpuCount { get; set; }
    public double? GpuTemp { get; set; }
    public double? GpuHotSpotTemp { get; set; }
    public double? GpuLoad { get; set; }
    public double? GpuPowerWatts { get; set; }
    public double? GpuClockMhz { get; set; }
    public double? GpuVramUsedGb { get; set; }
    public double? GpuFanRpm { get; set; }

    // RAM, Mainboard & Storage Parameter
    public double? RamUsedGb { get; set; }
    public double? RamTotalGb { get; set; }
    public double? RamLoadPercent { get; set; }
    public double? MainboardTemp { get; set; }
    public double? StorageTemp { get; set; }
}

public class ProtocolEventDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string TimestampDe { get; set; } = string.Empty; // z.B. "06.11.2026 12:35:14 (MEZ)"
    public string EventType { get; set; } = "INFO"; // "ABSTURZ" | "RECONNECT" | "WARNUNG" | "AUFZEICHNUNG"
    public string Keyword { get; set; } = string.Empty; // z.B. "Absturz"
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string? OrderNumber { get; set; }
    public string HostPort { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LastKnownMetrics { get; set; }
    public string? DowntimeDuration { get; set; }
}

public class RecordingStatusDto
{
    public bool IsRecording { get; set; } = true;
    public string StartedAtDe { get; set; } = string.Empty;
    public string CurrentTimeDe { get; set; } = string.Empty;
    public string TimezoneName { get; set; } = "Europe/Berlin (Deutsche Zeitzone)";
    public int IntervalSeconds { get; set; } = 3;
    public int TotalSamplesRecorded { get; set; }
    public int CrashCount { get; set; }
    public string LogFilePath { get; set; } = string.Empty;
    public List<string> SimulatedOfflineDeviceIds { get; set; } = new();
    public List<ProtocolEventDto> ProtocolEntries { get; set; } = new();
    public List<TelemetrySnapshotPointDto> RecentSamples { get; set; } = new();
}

public class AddDeviceRequest
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 8085;
    public string? OrderNumber { get; set; }
    public string? TestType { get; set; }
}

public class ScanNetworkRequest
{
    public string? SubnetPrefix { get; set; }
    public int Port { get; set; } = 8085;
    public int TimeoutMs { get; set; } = 350;
}

public class ScanNetworkResponse
{
    public string ScannedRange { get; set; } = string.Empty;
    public int Port { get; set; } = 8085;
    public int HostsScanned { get; set; }
    public int DurationMs { get; set; }
    public List<HardwareDeviceReportDto> DiscoveredDevices { get; set; } = new();
}

public class DeviceDiscoveryAlert
{
    public string AlertId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DeviceId { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 8085;
    public string Name { get; set; } = string.Empty;
    public string? SystemModel { get; set; }
    public string? CurrentOrderNumber { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public string DiscoveredAtDe { get; set; } = string.Empty;
    public string Message { get; set; } = "Neues Gerät verfügbar";
}

public class SetDeviceOrderNumberRequest
{
    public string OrderNumber { get; set; } = string.Empty;
}

public class DismissDiscoveryAlertRequest
{
    public string? AlertId { get; set; }
    public string? DeviceId { get; set; }
}

public class HttpDiscoverDeviceRequest
{
    public string? Host { get; set; }
    public int Port { get; set; } = 8085;
    public string? MachineName { get; set; }
    public string? SystemModel { get; set; }
    public string? OrderNumber { get; set; }
    public string? Version { get; set; } = "2.1.0";
}

