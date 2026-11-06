using System.Globalization;
using System.Text;
using System.Text.Json;
using HardwareCheck.Api.Models;

namespace HardwareCheck.Api.Services;

public static class GermanTimeHelper
{
    private static readonly TimeZoneInfo BerlinTimeZone = ResolveBerlinTimeZone();
    private static readonly CultureInfo GermanCulture = new("de-DE");

    private static TimeZoneInfo ResolveBerlinTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }
    }

    public static DateTime GetGermanNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BerlinTimeZone);
    }

    public static string GetZoneAbbreviation(DateTime utcNow)
    {
        bool isDst = BerlinTimeZone.IsDaylightSavingTime(utcNow);
        return isDst ? "MESZ" : "MEZ";
    }

    public static string FormatFull(DateTime? utcTime = null)
    {
        DateTime u = utcTime ?? DateTime.UtcNow;
        if (u.Kind != DateTimeKind.Utc)
            u = DateTime.SpecifyKind(u, DateTimeKind.Utc);

        DateTime de = TimeZoneInfo.ConvertTimeFromUtc(u, BerlinTimeZone);
        string abbr = GetZoneAbbreviation(u);
        return $"{de.ToString("dd.MM.yyyy HH:mm:ss", GermanCulture)} ({abbr})";
    }

    public static string FormatShortTime(DateTime? utcTime = null)
    {
        DateTime u = utcTime ?? DateTime.UtcNow;
        if (u.Kind != DateTimeKind.Utc)
            u = DateTime.SpecifyKind(u, DateTimeKind.Utc);

        DateTime de = TimeZoneInfo.ConvertTimeFromUtc(u, BerlinTimeZone);
        return de.ToString("HH:mm:ss", GermanCulture);
    }
}

public class HardwareRecordingService
{
    private readonly string _dataDir;
    private readonly string _logFilePath;
    private readonly string _jsonHistoryPath;
    private readonly object _lock = new();

    private bool _isRecording = true;
    private string _startedAtDe;
    private int _totalSamplesRecorded = 0;

    private readonly List<ProtocolEventDto> _protocolEntries = new();
    private readonly List<TelemetrySnapshotPointDto> _recentSamples = new();

    // State tracking per device ID for crash / connection loss detection
    private readonly Dictionary<string, bool> _lastOnlineState = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastMetricsSummary = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _offlineSinceUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _simulatedOfflineDevices = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastSampleSavedUtc = DateTime.MinValue;

    public HardwareRecordingService(IWebHostEnvironment env)
    {
        _dataDir = Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(_dataDir);

        _logFilePath = Path.Combine(_dataDir, "absturz_protokoll.log");
        _jsonHistoryPath = Path.Combine(_dataDir, "recording_history.json");
        _startedAtDe = GermanTimeHelper.FormatFull();

        LoadPersistedHistory();

        if (_protocolEntries.Count == 0)
        {
            AddProtocolEventInternal(new ProtocolEventDto
            {
                TimestampDe = GermanTimeHelper.FormatFull(),
                EventType = "AUFZEICHNUNG",
                Keyword = "Aufzeichnung gestartet",
                DeviceId = "system",
                DeviceName = "CPM Hardware-Check",
                HostPort = "Port 8085",
                Message = "Aufzeichnung und Verbindungsüberwachung (Port 8085) in deutscher Zeitzone (Europe/Berlin) gestartet."
            });
        }
    }

    public bool IsRecording => _isRecording;

    public int CrashCount
    {
        get
        {
            lock (_lock)
            {
                return _protocolEntries.Count(e => e.EventType == "ABSTURZ");
            }
        }
    }

    public bool IsDeviceSimulatedOffline(string deviceId)
    {
        lock (_lock)
        {
            return _simulatedOfflineDevices.Contains(deviceId);
        }
    }

    public void SetRecordingState(bool enabled)
    {
        lock (_lock)
        {
            if (_isRecording == enabled) return;
            _isRecording = enabled;
            if (enabled)
            {
                _startedAtDe = GermanTimeHelper.FormatFull();
            }

            AddProtocolEventInternal(new ProtocolEventDto
            {
                TimestampDe = GermanTimeHelper.FormatFull(),
                EventType = "AUFZEICHNUNG",
                Keyword = enabled ? "Aufzeichnung aktiv" : "Aufzeichnung pausiert",
                DeviceId = "system",
                DeviceName = "CPM Hardware-Check",
                HostPort = "Port 8085",
                Message = enabled
                    ? "Hardware-Aufzeichnung & Absturz-Überwachung wurde gestartet."
                    : "Hardware-Aufzeichnung wurde pausiert."
            });
        }
    }

    /// <summary>
    /// Ermöglicht das gezielte Simulieren/Testen eines Verbindungsabbruchs ("Absturz") für ein Gerät
    /// oder das Wiederherstellen der Verbindung.
    /// </summary>
    public void ToggleSimulatedCrash(string deviceId, bool forceOffline)
    {
        lock (_lock)
        {
            if (forceOffline)
                _simulatedOfflineDevices.Add(deviceId);
            else
                _simulatedOfflineDevices.Remove(deviceId);
        }
    }

    /// <summary>
    /// Verarbeitet den aktuellen Geräte-Status, zeichnet Messpunkte auf und protokolliert
    /// jeden Verbindungsabbruch sofort mit dem Wort "Absturz" in deutscher Zeitzone.
    /// </summary>
    public void ProcessDashboardSnapshot(List<HardwareDeviceReportDto> devices, bool forceRecordSample = false)
    {
        DateTime nowUtc = DateTime.UtcNow;
        string nowFullDe = GermanTimeHelper.FormatFull(nowUtc);
        string nowShortDe = GermanTimeHelper.FormatShortTime(nowUtc);

        lock (_lock)
        {
            bool shouldRecordTimeSample = _isRecording &&
                (forceRecordSample || (nowUtc - _lastSampleSavedUtc).TotalSeconds >= 2.5);

            if (shouldRecordTimeSample)
            {
                _lastSampleSavedUtc = nowUtc;
            }

            foreach (var dev in devices)
            {
                bool hadPreviousState = _lastOnlineState.TryGetValue(dev.Id, out bool wasOnline);

                if (dev.IsOnline)
                {
                    // Merke letzte bekannte Messwerte vor einem eventuellen Absturz
                    string metricsText = FormatMetricsSummary(dev.Summary);
                    _lastMetricsSummary[dev.Id] = metricsText;

                    // Falls das Gerät zuvor nach einem Absturz offline war -> Reconnect protokollieren
                    if (hadPreviousState && !wasOnline)
                    {
                        string downtimeStr = "unbekannt";
                        if (_offlineSinceUtc.TryGetValue(dev.Id, out DateTime crashTime))
                        {
                            var diff = nowUtc - crashTime;
                            downtimeStr = diff.TotalMinutes >= 1
                                ? $"{(int)diff.TotalMinutes} Min. {diff.Seconds} Sek."
                                : $"{Math.Max(1, (int)diff.TotalSeconds)} Sek.";
                            _offlineSinceUtc.Remove(dev.Id);
                        }

                        AddProtocolEventInternal(new ProtocolEventDto
                        {
                            TimestampDe = nowFullDe,
                            EventType = "RECONNECT",
                            Keyword = "Verbindung wiederhergestellt",
                            DeviceId = dev.Id,
                            DeviceName = dev.Name,
                            HostPort = $"{dev.Host}:{dev.Port}",
                            DowntimeDuration = downtimeStr,
                            Message = $"Verbindung nach Absturz wiederhergestellt: Gerät '{dev.Name}' ({dev.Host}:{dev.Port}) antwortet wieder auf Port {dev.Port} (Ausfallzeit: {downtimeStr})."
                        });
                    }

                    _lastOnlineState[dev.Id] = true;
                }
                else
                {
                    // Gerät ist NICHT erreichbar (Offline / Verbindung weg) -> als "Absturz" protokollieren!
                    if (!hadPreviousState || wasOnline)
                    {
                        _offlineSinceUtc[dev.Id] = nowUtc;
                        _lastOnlineState[dev.Id] = false;

                        _lastMetricsSummary.TryGetValue(dev.Id, out string? lastMetrics);
                        if (string.IsNullOrWhiteSpace(lastMetrics))
                        {
                            lastMetrics = "Keine vorherigen Sensorwerte aufgezeichnet";
                        }

                        AddProtocolEventInternal(new ProtocolEventDto
                        {
                            TimestampDe = nowFullDe,
                            EventType = "ABSTURZ",
                            Keyword = "Absturz",
                            DeviceId = dev.Id,
                            DeviceName = dev.Name,
                            HostPort = $"{dev.Host}:{dev.Port}",
                            LastKnownMetrics = lastMetrics,
                            Message = $"ABSTURZ: Verbindung zu Gerät '{dev.Name}' ({dev.Host}:{dev.Port}) war weg um {nowFullDe}! ({dev.ErrorMessage ?? "Keine Antwort auf Port 8085"})"
                        });

                        // Bei einem Absturz immer sofort auch einen Messpunkt in der Aufzeichnung speichern
                        RecordSamplePointInternal(dev, nowFullDe, nowShortDe);
                        continue;
                    }
                }

                if (shouldRecordTimeSample)
                {
                    RecordSamplePointInternal(dev, nowFullDe, nowShortDe);
                }
            }

            if (shouldRecordTimeSample)
            {
                SavePersistedHistoryInternal();
            }
        }
    }

    public RecordingStatusDto GetStatus()
    {
        lock (_lock)
        {
            return new RecordingStatusDto
            {
                IsRecording = _isRecording,
                StartedAtDe = _startedAtDe,
                CurrentTimeDe = GermanTimeHelper.FormatFull(),
                TimezoneName = "Europe/Berlin (MEZ/MESZ)",
                IntervalSeconds = 3,
                TotalSamplesRecorded = _totalSamplesRecorded,
                CrashCount = _protocolEntries.Count(e => e.EventType == "ABSTURZ"),
                LogFilePath = _logFilePath,
                SimulatedOfflineDeviceIds = _simulatedOfflineDevices.ToList(),
                ProtocolEntries = _protocolEntries.ToList(),
                RecentSamples = _recentSamples.ToList()
            };
        }
    }

    public void ClearProtocolAndSamples()
    {
        lock (_lock)
        {
            _protocolEntries.Clear();
            _recentSamples.Clear();
            _totalSamplesRecorded = 0;

            AddProtocolEventInternal(new ProtocolEventDto
            {
                TimestampDe = GermanTimeHelper.FormatFull(),
                EventType = "AUFZEICHNUNG",
                Keyword = "Protokoll zurückgesetzt",
                DeviceId = "system",
                DeviceName = "CPM Hardware-Check",
                HostPort = "Port 8085",
                Message = "Aufzeichnung und Absturz-Protokoll wurden zurückgesetzt."
            });

            SavePersistedHistoryInternal();
        }
    }

    public string BuildLogFileContent()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("  CPM HARDWARE-CHECK - AUFZEICHNUNG & ABSTURZ-PROTOKOLL (DEUTSCHE ZEITZONE)");
            sb.AppendLine($"  Exportiert am: {GermanTimeHelper.FormatFull()}");
            sb.AppendLine($"  Erfasste Abstürze (Verbindungsabbrüche): {_protocolEntries.Count(e => e.EventType == "ABSTURZ")}");
            sb.AppendLine($"  Aufgezeichnete Messpunkte: {_totalSamplesRecorded}");
            sb.AppendLine("================================================================================");
            sb.AppendLine();
            sb.AppendLine("--- 1. EREIGNIS- & ABSTURZ-PROTOKOLL ---");

            foreach (var entry in _protocolEntries)
            {
                sb.AppendLine($"[{entry.TimestampDe}] [{entry.EventType}] ({entry.Keyword}) - {entry.DeviceName} [{entry.HostPort}]");
                sb.AppendLine($"    -> {entry.Message}");
                if (!string.IsNullOrWhiteSpace(entry.LastKnownMetrics))
                {
                    sb.AppendLine($"    -> Letzte Hardware-Werte vor Absturz: {entry.LastKnownMetrics}");
                }
                if (!string.IsNullOrWhiteSpace(entry.DowntimeDuration))
                {
                    sb.AppendLine($"    -> Dauer des Verbindungsausfalls: {entry.DowntimeDuration}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("--- 2. AUFGEZEICHNETE HARDWARE-MESSPUNKTE (ZEITVERLAUF) ---");
            sb.AppendLine("Zeitpunkt (DE)\tGerät\tEndpunkt\tStatus\tCPU Temp\tCPU Last\tCPU Takt\tCPU Watt\tGPU Temp\tGPU HotSpot\tGPU Last\tGPU Watt\tRAM Belegt\tMainboard\tSSD Temp");

            foreach (var s in _recentSamples)
            {
                string cpuT = s.CpuTemp.HasValue ? $"{s.CpuTemp.Value:F1} °C" : "-";
                string cpuL = s.CpuLoad.HasValue ? $"{s.CpuLoad.Value:F1} %" : "-";
                string cpuClk = s.CpuClockMhz.HasValue ? $"{s.CpuClockMhz.Value:F0} MHz" : "-";
                string cpuW = s.CpuPowerWatts.HasValue ? $"{s.CpuPowerWatts.Value:F1} W" : "-";
                string gpuT = s.GpuTemp.HasValue ? $"{s.GpuTemp.Value:F1} °C" : "-";
                string gpuHs = s.GpuHotSpotTemp.HasValue ? $"{s.GpuHotSpotTemp.Value:F1} °C" : "-";
                string gpuL = s.GpuLoad.HasValue ? $"{s.GpuLoad.Value:F1} %" : "-";
                string gpuW = s.GpuPowerWatts.HasValue ? $"{s.GpuPowerWatts.Value:F1} W" : "-";
                string ram = s.RamUsedGb.HasValue ? $"{s.RamUsedGb.Value:F1} GB ({s.RamLoadPercent ?? 0:F0}%)" : "-";
                string mbT = s.MainboardTemp.HasValue ? $"{s.MainboardTemp.Value:F1} °C" : "-";
                string ssdT = s.StorageTemp.HasValue ? $"{s.StorageTemp.Value:F1} °C" : "-";

                sb.AppendLine($"{s.TimestampDe}\t{s.DeviceName}\t{s.HostPort}\t{s.StatusText}\t{cpuT}\t{cpuL}\t{cpuClk}\t{cpuW}\t{gpuT}\t{gpuHs}\t{gpuL}\t{gpuW}\t{ram}\t{mbT}\t{ssdT}");
            }

            return sb.ToString();
        }
    }

    private void RecordSamplePointInternal(HardwareDeviceReportDto dev, string fullTimeDe, string shortTimeDe)
    {
        var point = new TelemetrySnapshotPointDto
        {
            TimestampDe = fullTimeDe,
            TimeShortDe = shortTimeDe,
            DeviceId = dev.Id,
            DeviceName = dev.Name,
            HostPort = $"{dev.Host}:{dev.Port}",
            IsOnline = dev.IsOnline,
            StatusText = dev.IsOnline ? "Online" : "Absturz (Verbindung weg)",
            CpuTemp = dev.IsOnline ? dev.Summary.CpuTemp : null,
            CpuMaxCoreTemp = dev.IsOnline ? dev.Summary.CpuMaxCoreTemp : null,
            CpuLoad = dev.IsOnline ? dev.Summary.CpuLoad : null,
            CpuClockMhz = dev.IsOnline ? dev.Summary.CpuClockMhz : null,
            CpuPowerWatts = dev.IsOnline ? dev.Summary.CpuPowerWatts : null,
            CpuVoltage = dev.IsOnline ? dev.Summary.CpuVoltage : null,
            CpuFanRpm = dev.IsOnline ? dev.Summary.CpuFanRpm : null,
            GpuTemp = dev.IsOnline ? dev.Summary.GpuTemp : null,
            GpuHotSpotTemp = dev.IsOnline ? dev.Summary.GpuHotSpotTemp : null,
            GpuLoad = dev.IsOnline ? dev.Summary.GpuLoad : null,
            GpuPowerWatts = dev.IsOnline ? dev.Summary.GpuPowerWatts : null,
            GpuClockMhz = dev.IsOnline ? dev.Summary.GpuClockMhz : null,
            GpuVramUsedGb = dev.IsOnline ? dev.Summary.GpuVramUsedGb : null,
            GpuFanRpm = dev.IsOnline ? dev.Summary.GpuFanRpm : null,
            RamUsedGb = dev.IsOnline ? dev.Summary.RamUsedGb : null,
            RamTotalGb = dev.IsOnline ? dev.Summary.RamTotalGb : null,
            RamLoadPercent = dev.IsOnline ? dev.Summary.RamLoadPercent : null,
            MainboardTemp = dev.IsOnline ? dev.Summary.MainboardTemp : null,
            StorageTemp = dev.IsOnline ? dev.Summary.StorageTemp : null
        };

        _recentSamples.Insert(0, point);
        _totalSamplesRecorded++;

        // Behalte die letzten 450 Messpunkte im Speicher für flüssige Verlaufsgrafiken
        if (_recentSamples.Count > 450)
        {
            _recentSamples.RemoveRange(450, _recentSamples.Count - 450);
        }
    }

    private void AddProtocolEventInternal(ProtocolEventDto evt)
    {
        _protocolEntries.Insert(0, evt);
        if (_protocolEntries.Count > 200)
        {
            _protocolEntries.RemoveRange(200, _protocolEntries.Count - 200);
        }

        try
        {
            var logLine = new StringBuilder();
            logLine.Append($"[{evt.TimestampDe}] [{evt.EventType}] {evt.Keyword} | {evt.DeviceName} ({evt.HostPort}): {evt.Message}");
            if (!string.IsNullOrWhiteSpace(evt.LastKnownMetrics))
            {
                logLine.Append($" | Letzte Werte vor Absturz: {evt.LastKnownMetrics}");
            }
            logLine.AppendLine();

            File.AppendAllText(_logFilePath, logLine.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Ignore file write errors
        }

        SavePersistedHistoryInternal();
    }

    private static string FormatMetricsSummary(QuickMetricsDto q)
    {
        var parts = new List<string>();
        if (q.CpuTemp.HasValue || q.CpuLoad.HasValue)
        {
            parts.Add($"CPU ({q.CpuName}): {(q.CpuTemp.HasValue ? $"{q.CpuTemp.Value:F1} °C" : "-")} / {(q.CpuLoad.HasValue ? $"{q.CpuLoad.Value:F1} %" : "-")}{(q.CpuPowerWatts.HasValue ? $" / {q.CpuPowerWatts.Value:F1} W" : "")}");
        }
        if (q.GpuTemp.HasValue || q.GpuLoad.HasValue)
        {
            parts.Add($"GPU ({q.GpuName}): {(q.GpuTemp.HasValue ? $"{q.GpuTemp.Value:F1} °C" : "-")} / {(q.GpuLoad.HasValue ? $"{q.GpuLoad.Value:F1} %" : "-")}");
        }
        if (q.RamUsedGb.HasValue)
        {
            parts.Add($"RAM: {q.RamUsedGb.Value:F1} GB ({q.RamLoadPercent ?? 0:F0} %)");
        }
        return parts.Count > 0 ? string.Join(" | ", parts) : "Keine Sensordaten";
    }

    private class PersistedRecordingData
    {
        public bool IsRecording { get; set; } = true;
        public string StartedAtDe { get; set; } = string.Empty;
        public int TotalSamplesRecorded { get; set; }
        public List<ProtocolEventDto> ProtocolEntries { get; set; } = new();
        public List<TelemetrySnapshotPointDto> RecentSamples { get; set; } = new();
    }

    private void LoadPersistedHistory()
    {
        if (!File.Exists(_jsonHistoryPath)) return;
        try
        {
            string json = File.ReadAllText(_jsonHistoryPath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<PersistedRecordingData>(json);
            if (data != null)
            {
                _isRecording = data.IsRecording;
                if (!string.IsNullOrWhiteSpace(data.StartedAtDe))
                    _startedAtDe = data.StartedAtDe;
                _totalSamplesRecorded = data.TotalSamplesRecorded;
                if (data.ProtocolEntries != null)
                    _protocolEntries.AddRange(data.ProtocolEntries);
                if (data.RecentSamples != null)
                    _recentSamples.AddRange(data.RecentSamples);
            }
        }
        catch
        {
            // Keep defaults
        }
    }

    private void SavePersistedHistoryInternal()
    {
        try
        {
            var data = new PersistedRecordingData
            {
                IsRecording = _isRecording,
                StartedAtDe = _startedAtDe,
                TotalSamplesRecorded = _totalSamplesRecorded,
                ProtocolEntries = _protocolEntries,
                RecentSamples = _recentSamples
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_jsonHistoryPath, json, Encoding.UTF8);
        }
        catch
        {
            // Ignore write errors
        }
    }
}

