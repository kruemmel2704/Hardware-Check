using System.Globalization;
using System.Text;
using System.Text.Json;
using HardwareCheck.Api.Models;

namespace HardwareCheck.Api.Services;

/// <summary>
/// Speichert die vom DiagnoseTool gelieferten Absturzanalysen (Windows-Ereignisanzeige) dauerhaft
/// unter data/crash_reports und stellt Kurzfassungen, Vollberichte und Textexporte bereit.
/// </summary>
public class CrashReportStore
{
    private readonly string _dir;
    private readonly object _lock = new();
    private readonly List<CrashReportSummaryDto> _summaries = new();

    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = false };

    public CrashReportStore(IWebHostEnvironment env)
    {
        _dir = Path.Combine(env.ContentRootPath, "data", "crash_reports");
        Directory.CreateDirectory(_dir);
        LoadIndex();
    }

    private class StoredCrashReport
    {
        public CrashReportSummaryDto Summary { get; set; } = new();
        public JsonElement Report { get; set; }
    }

    private void LoadIndex()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<StoredCrashReport>(File.ReadAllText(file, Encoding.UTF8));
                    if (stored?.Summary != null && !string.IsNullOrWhiteSpace(stored.Summary.ReportId))
                    {
                        _summaries.Add(stored.Summary);
                    }
                }
                catch { }
            }
            _summaries.Sort((a, b) => b.ReceivedUtc.CompareTo(a.ReceivedUtc));
        }
        catch { }
    }

    public bool Contains(string reportId)
    {
        lock (_lock) return _summaries.Any(s => s.ReportId.Equals(reportId, StringComparison.OrdinalIgnoreCase));
    }

    public CrashReportSummaryDto Add(string deviceId, string deviceName, string? orderNumber, JsonElement report)
    {
        var summary = BuildSummary(deviceId, deviceName, orderNumber, report);

        lock (_lock)
        {
            var existing = _summaries.FirstOrDefault(s => s.ReportId.Equals(summary.ReportId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            _summaries.Insert(0, summary);
            try
            {
                var stored = new StoredCrashReport { Summary = summary, Report = report };
                File.WriteAllText(PathFor(summary.ReportId), JsonSerializer.Serialize(stored, WriteOpts), Encoding.UTF8);
            }
            catch { }

            // Höchstens 500 Berichte behalten
            while (_summaries.Count > 500)
            {
                var old = _summaries[^1];
                _summaries.RemoveAt(_summaries.Count - 1);
                try { File.Delete(PathFor(old.ReportId)); } catch { }
            }
        }

        return summary;
    }

    private string PathFor(string reportId)
    {
        string safe = string.Concat(reportId.Where(char.IsLetterOrDigit));
        return Path.Combine(_dir, $"{safe}.json");
    }

    public List<CrashReportSummaryDto> GetSummaries(string? deviceId = null)
    {
        lock (_lock)
        {
            return _summaries
                .Where(s => deviceId == null || s.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public CrashReportSummaryDto? GetLatestForDevice(string deviceId)
    {
        lock (_lock)
        {
            return _summaries.FirstOrDefault(s => s.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) && s.Scope == "crash");
        }
    }

    public (CrashReportSummaryDto Summary, JsonElement Report)? Get(string reportId)
    {
        lock (_lock)
        {
            if (!_summaries.Any(s => s.ReportId.Equals(reportId, StringComparison.OrdinalIgnoreCase))) return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredCrashReport>(File.ReadAllText(PathFor(reportId), Encoding.UTF8));
            if (stored == null) return null;
            return (stored.Summary, stored.Report);
        }
        catch
        {
            return null;
        }
    }

    public bool Delete(string reportId)
    {
        lock (_lock)
        {
            int removed = _summaries.RemoveAll(s => s.ReportId.Equals(reportId, StringComparison.OrdinalIgnoreCase));
            try { File.Delete(PathFor(reportId)); } catch { }
            return removed > 0;
        }
    }

    // ------------------------------------------------------------------

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;

    private static int Count(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;

    private static CrashReportSummaryDto BuildSummary(string deviceId, string deviceName, string? orderNumber, JsonElement r)
    {
        string? bcCode = null, bcName = null;
        if (r.TryGetProperty("bugCheck", out var bc) && bc.ValueKind == JsonValueKind.Object)
        {
            bcCode = Str(bc, "codeHex");
            bcName = Str(bc, "name");
        }

        if (string.IsNullOrWhiteSpace(orderNumber) && r.TryGetProperty("previousSession", out var ps) && ps.ValueKind == JsonValueKind.Object)
        {
            orderNumber = Str(ps, "orderNumber");
        }

        return new CrashReportSummaryDto
        {
            ReportId = Str(r, "reportId") ?? Guid.NewGuid().ToString("N")[..12],
            DeviceId = deviceId,
            DeviceName = deviceName,
            OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? null : orderNumber,
            ReceivedUtc = DateTime.UtcNow,
            ReceivedAtDe = GermanTimeHelper.FormatFull(),
            Scope = Str(r, "scope") ?? "crash",
            CrashType = Str(r, "crashType") ?? "unknown",
            CrashTypeDisplay = Str(r, "crashTypeDisplay") ?? "",
            IsRealCrash = Bool(r, "isRealCrash"),
            IsSystemCrash = Bool(r, "isSystemCrash"),
            Severity = Str(r, "severity") ?? "INFO",
            SeverityScore = Int(r, "severityScore"),
            Category = Str(r, "category") ?? "",
            Headline = Str(r, "headline") ?? "Absturzanalyse",
            Summary = Str(r, "summary") ?? "",
            CrashTimeLocal = Str(r, "crashTimeLocal"),
            BugCheckCode = bcCode,
            BugCheckName = bcName,
            EventCount = Count(r, "events"),
            FindingCount = Count(r, "findings")
        };
    }

    /// <summary>Lesbarer Textbericht (für Download/Archiv).</summary>
    public static string BuildText(CrashReportSummaryDto s, JsonElement r)
    {
        var sb = new StringBuilder();
        void Line(string label, string? value) => sb.AppendLine($"{label,-22}{value ?? "-"}");
        void List(string title, string prop)
        {
            if (!r.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return;
            sb.AppendLine();
            sb.AppendLine(title + ":");
            foreach (var item in arr.EnumerateArray()) sb.AppendLine("  - " + item.GetString());
        }

        sb.AppendLine("================================================================================");
        sb.AppendLine("  CPM HARDWARE-CHECK - ABSTURZANALYSE (WINDOWS-EREIGNISANZEIGE)");
        sb.AppendLine("================================================================================");
        Line("Bericht-ID:", s.ReportId);
        Line("Empfangen:", s.ReceivedAtDe);
        Line("Gerät:", $"{s.DeviceName} ({s.DeviceId})");
        Line("Auftrags-Nr.:", s.OrderNumber);
        Line("Rechner:", Str(r, "machineName"));
        Line("Betriebssystem:", Str(r, "osDescription"));
        Line("DiagnoseTool:", Str(r, "appVersion"));
        Line("Systemstart:", Str(r, "systemBootTimeLocal"));
        Line("Absturzzeitpunkt:", s.CrashTimeLocal);
        sb.AppendLine();
        Line("ERGEBNIS:", s.Headline);
        Line("Art:", s.CrashTypeDisplay);
        Line("Kritikalität:", $"{s.Severity} ({s.SeverityScore}/100)");
        Line("", Str(r, "severityExplanation"));
        Line("Kategorie:", s.Category);
        sb.AppendLine();
        sb.AppendLine("Zusammenfassung:");
        sb.AppendLine("  " + s.Summary);

        if (r.TryGetProperty("bugCheck", out var bc) && bc.ValueKind == JsonValueKind.Object)
        {
            sb.AppendLine();
            sb.AppendLine("Bluescreen (BugCheck):");
            sb.AppendLine($"  Code:      {Str(bc, "codeHex")} {Str(bc, "name")}");
            sb.AppendLine($"  Bedeutung: {Str(bc, "description")}");
            if (bc.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0)
                sb.AppendLine($"  Parameter: {string.Join(", ", p.EnumerateArray().Select(x => x.GetString()))}");
            if (!string.IsNullOrWhiteSpace(Str(bc, "dumpPath"))) sb.AppendLine($"  Speicherabbild: {Str(bc, "dumpPath")}");
        }

        if (r.TryGetProperty("previousSession", out var ps) && ps.ValueKind == JsonValueKind.Object)
        {
            if (ps.TryGetProperty("lastActivity", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                string F(string n, string u) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? $"{v.GetDouble().ToString("F1", CultureInfo.InvariantCulture)} {u}" : "-";
                string B(string n) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True ? "läuft" : "aus";
                sb.AppendLine();
                sb.AppendLine("Zustand kurz vor dem Ausfall:");
                sb.AppendLine($"  Stresstests: CPU={B("isCpuStressRunning")}, RAM={B("isRamStressRunning")}, GPU={B("isGpuStressRunning")}, Akkutest={B("isBatteryTestRunning")}");
                sb.AppendLine($"  CPU {F("cpuTemp", "°C")} / {F("cpuLoad", "%")} / {F("cpuPowerWatts", "W")} | GPU {F("gpuTemp", "°C")} (Hotspot {F("gpuHotSpotTemp", "°C")}) / {F("gpuLoad", "%")} / {F("gpuPowerWatts", "W")} | RAM {F("ramPercent", "%")}");
            }
            if (!string.IsNullOrWhiteSpace(Str(ps, "unhandledException")))
            {
                sb.AppendLine();
                sb.AppendLine("Unbehandelte Ausnahme im DiagnoseTool:");
                sb.AppendLine("  " + Str(ps, "unhandledException"));
            }
        }

        List("Wahrscheinliche Ursachen", "likelyCauses");
        List("Empfohlene Maßnahmen", "recommendations");
        List("Technische Details", "technicalDetails");

        sb.AppendLine();
        sb.AppendLine($"Hardwarefehler (WHEA) letzte 30 Tage: {Int(r, "wheaFatalLast30Days")} schwerwiegend, {Int(r, "wheaCorrectedLast30Days")} korrigiert");
        sb.AppendLine($"Unerwartete Herunterfahren letzte 30 Tage: {Int(r, "unexpectedShutdownsLast30Days")}");

        if (r.TryGetProperty("findings", out var findings) && findings.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine();
            sb.AppendLine("--- BEFUNDE ---");
            foreach (var f in findings.EnumerateArray())
            {
                string evId = f.TryGetProperty("eventId", out var ev) && ev.ValueKind == JsonValueKind.Number ? ev.GetInt32().ToString() : "-";
                sb.AppendLine($"[{Str(f, "timeLocal") ?? "-"}] [{Str(f, "severity")}] [{Str(f, "category")}] {Str(f, "title")} ({Str(f, "log")}/{Str(f, "provider")} #{evId})");
                sb.AppendLine($"    -> {Str(f, "interpretation")}");
                if (!string.IsNullOrWhiteSpace(Str(f, "evidence"))) sb.AppendLine($"    -> Beleg: {Str(f, "evidence")}");
            }
        }

        if (r.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine();
            sb.AppendLine($"--- VOLLSTÄNDIGER AUSZUG EREIGNISANZEIGE ({events.GetArrayLength()} Ereignisse) ---");
            sb.AppendLine("Zeit\tProtokoll\tEbene\tQuelle\tID\tMeldung");
            foreach (var e in events.EnumerateArray().Reverse())
            {
                string evId = e.TryGetProperty("eventId", out var ev) && ev.ValueKind == JsonValueKind.Number ? ev.GetInt32().ToString() : "-";
                string msg = (Str(e, "message") ?? "").Replace("\r", " ").Replace("\n", " ");
                sb.AppendLine($"{Str(e, "timeLocal")}\t{Str(e, "log")}\t{Str(e, "levelName")}\t{Str(e, "provider")}\t{evId}\t{msg}");
            }
        }

        if (!string.IsNullOrWhiteSpace(Str(r, "error")))
        {
            sb.AppendLine();
            sb.AppendLine("HINWEIS: " + Str(r, "error"));
        }

        return sb.ToString();
    }
}
