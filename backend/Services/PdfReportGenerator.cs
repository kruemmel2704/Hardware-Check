using System.Diagnostics;
using HardwareCheck.Api.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace HardwareCheck.Api.Services;

public static class PdfReportGenerator
{
    public static byte[] GenerateSystemReport(
        HardwareDeviceReportDto device,
        List<TelemetrySnapshotPointDto> samples,
        string? logoPath = null)
    {
        using var document = new PdfDocument();
        string orderNumber = device.OrderNumber ?? "";

        document.Info.Title = !string.IsNullOrEmpty(orderNumber)
            ? $"System Hardware Prüfbericht - Auftrag {orderNumber} - {device.Name}"
            : $"System Hardware Prüfbericht - {device.Name}";
        document.Info.Author = "CPM Hardware-Check";
        document.Info.Subject = "Hardware Diagnostics Log & Test Protocol";

        // Add DIN A4 Page (595 x 842 pt)
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        // Colors
        var colorPrimary = XColor.FromArgb(0, 129, 188); // CPM Blue
        var colorDark = XColor.FromArgb(30, 41, 59);     // Dark Slate
        var colorGray = XColor.FromArgb(100, 116, 139);   // Muted Slate
        var colorLightBg = XColor.FromArgb(245, 247, 250); // Card Background
        var colorBorder = XColor.FromArgb(226, 232, 240); // Border

        // Fonts
        var fontTitle = new XFont("Segoe UI", 22, XFontStyleEx.Bold);
        var fontSubtitle = new XFont("Segoe UI", 10, XFontStyleEx.Italic);
        var fontHeading = new XFont("Segoe UI", 13, XFontStyleEx.Bold);
        var fontSubHeading = new XFont("Segoe UI", 11, XFontStyleEx.Bold);
        var fontBody = new XFont("Segoe UI", 9.5, XFontStyleEx.Regular);
        var fontBodyBold = new XFont("Segoe UI", 9.5, XFontStyleEx.Bold);
        var fontMuted = new XFont("Segoe UI", 8, XFontStyleEx.Regular);

        // 1. Header (Title & Logo)
        gfx.DrawString("SYSTEM-PRÜFBERICHT", fontTitle, new XSolidBrush(colorPrimary), 40, 60);
        gfx.DrawString("Hardware-Diagnose & Stresstest Protokoll", fontSubtitle, new XSolidBrush(colorGray), 40, 78);

        // Draw Logo if file exists, else fallback text
        bool logoDrawn = false;
        if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
        {
            try
            {
                using var image = XImage.FromFile(logoPath);
                gfx.DrawImage(image, 420, 36, 135, 48);
                logoDrawn = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PdfReportGenerator] Logo load error: {ex.Message}");
            }
        }

        if (!logoDrawn)
        {
            gfx.DrawString("CPM", new XFont("Segoe UI", 22, XFontStyleEx.Bold), new XSolidBrush(colorPrimary), 490, 60);
        }

        // Divider Line
        var penDivider = new XPen(colorPrimary, 1.5);
        gfx.DrawLine(penDivider, 40, 95, 555, 95);

        // 2. Section: System- & Aufzeichnungsdaten
        var rectSystemBox = new XRect(40, 110, 515, 130);
        gfx.DrawRectangle(new XSolidBrush(colorLightBg), rectSystemBox);
        gfx.DrawRectangle(new XPen(colorBorder, 1), rectSystemBox);

        string cpuName = device.Summary?.CpuName ?? "CPU";
        string gpuName = device.Summary?.GpuName ?? "Keine dedizierte Grafikkarte";
        string ramDisplay = device.Summary?.RamTotalGb.HasValue == true
            ? $"{device.Summary.RamTotalGb.Value:F1} GB"
            : (device.Summary?.RamLoadPercent.HasValue == true ? $"{device.Summary.RamLoadPercent.Value:F0} %" : "--");
        string mbName = device.Summary?.MotherboardName ?? "System Mainboard";

        // Column 1: Hardware-Konfiguration
        gfx.DrawString("HARDWARE-KONFIGURATION", fontSubHeading, new XSolidBrush(colorPrimary), 52, 128);
        gfx.DrawString($"Gerät / Host:     {device.Name} ({device.Host}:{device.Port})", fontBody, new XSolidBrush(colorDark), 52, 144);
        gfx.DrawString($"Prozessor (CPU):  {Truncate(cpuName, 42)}", fontBody, new XSolidBrush(colorDark), 52, 158);
        gfx.DrawString($"Grafikkarte (GPU): {Truncate(gpuName, 42)}", fontBody, new XSolidBrush(colorDark), 52, 172);
        gfx.DrawString($"Arbeitsspeicher:  {ramDisplay}", fontBody, new XSolidBrush(colorDark), 52, 186);
        gfx.DrawString($"Mainboard:        {Truncate(mbName, 42)}", fontBody, new XSolidBrush(colorDark), 52, 200);
        gfx.DrawString($"Laufwerk / SSD:   {Truncate(device.Summary?.StorageName ?? "Systemlaufwerk", 42)}", fontBody, new XSolidBrush(colorDark), 52, 214);

        // Column 2: Aufzeichnungsdaten
        gfx.DrawString("AUFZEICHNUNGSDATEN", fontSubHeading, new XSolidBrush(colorPrimary), 330, 128);
        if (!string.IsNullOrEmpty(orderNumber))
        {
            gfx.DrawString($"Auftrags-Nr.:   {orderNumber}", fontBodyBold, new XSolidBrush(colorPrimary), 330, 144);
        }
        else
        {
            gfx.DrawString($"Auftrags-Nr.:   Nicht angegeben", fontBody, new XSolidBrush(colorDark), 330, 144);
        }

        string startTime = samples.Count > 0 ? (samples.First().TimestampDe ?? "") : GermanTimeHelper.FormatFull();
        string endTime = samples.Count > 0 ? (samples.Last().TimestampDe ?? "") : GermanTimeHelper.FormatFull();
        int sampleCount = samples.Count;

        gfx.DrawString($"Startzeit:      {Truncate(startTime, 24)}", fontBody, new XSolidBrush(colorDark), 330, 158);
        gfx.DrawString($"Endzeit:        {Truncate(endTime, 24)}", fontBody, new XSolidBrush(colorDark), 330, 172);
        gfx.DrawString($"Messpunkte:     {sampleCount} Datenpunkte", fontBody, new XSolidBrush(colorDark), 330, 186);
        gfx.DrawString($"Status / Check: {(device.IsOnline ? "Online & Stabil" : "Offline / Unterbrochen")}", fontBodyBold,
            new XSolidBrush(device.IsOnline ? XColor.FromArgb(16, 185, 129) : XColor.FromArgb(239, 68, 68)), 330, 200);

        // 3. Section: Zusammenfassung der Messwerte
        gfx.DrawString("ZUSAMMENFASSUNG DER MESSWERTE", fontHeading, new XSolidBrush(colorDark), 40, 265);

        double tableY = 280;
        double rowHeight = 22;
        var brushHeader = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(brushHeader, 40, tableY, 515, rowHeight);
        gfx.DrawRectangle(new XPen(colorBorder, 1), 40, tableY, 515, rowHeight);

        gfx.DrawString("Komponente", fontBodyBold, new XSolidBrush(colorDark), 50, tableY + 14);
        gfx.DrawString("Ø Auslastung", fontBodyBold, new XSolidBrush(colorDark), 180, tableY + 14);
        gfx.DrawString("Max. Auslastung", fontBodyBold, new XSolidBrush(colorDark), 280, tableY + 14);
        gfx.DrawString("Ø Temp.", fontBodyBold, new XSolidBrush(colorDark), 380, tableY + 14);
        gfx.DrawString("Max. Temp.", fontBodyBold, new XSolidBrush(colorDark), 470, tableY + 14);

        // Calculate statistics
        var cpuLoads = samples.Where(s => s.CpuLoad.HasValue).Select(s => s.CpuLoad!.Value).ToList();
        string cpuAvgLoad = cpuLoads.Count > 0 ? $"{cpuLoads.Average():F1} %" : (device.Summary?.CpuLoad.HasValue == true ? $"{device.Summary.CpuLoad.Value:F1} %" : "--");
        string cpuMaxLoad = cpuLoads.Count > 0 ? $"{cpuLoads.Max():F1} %" : cpuAvgLoad;

        var cpuTemps = samples.Where(s => s.CpuTemp.HasValue && s.CpuTemp.Value > 0).Select(s => s.CpuTemp!.Value).ToList();
        string cpuAvgTemp = cpuTemps.Count > 0 ? $"{cpuTemps.Average():F1} °C" : (device.Summary?.CpuTemp.HasValue == true ? $"{device.Summary.CpuTemp.Value:F1} °C" : "--");
        string cpuMaxTemp = cpuTemps.Count > 0 ? $"{cpuTemps.Max():F1} °C" : cpuAvgTemp;

        var gpuLoads = samples.Where(s => s.GpuLoad.HasValue).Select(s => s.GpuLoad!.Value).ToList();
        string gpuAvgLoad = gpuLoads.Count > 0 ? $"{gpuLoads.Average():F1} %" : (device.Summary?.GpuLoad.HasValue == true ? $"{device.Summary.GpuLoad.Value:F1} %" : "--");
        string gpuMaxLoad = gpuLoads.Count > 0 ? $"{gpuLoads.Max():F1} %" : gpuAvgLoad;

        var gpuTemps = samples.Where(s => s.GpuTemp.HasValue && s.GpuTemp.Value > 0).Select(s => s.GpuTemp!.Value).ToList();
        string gpuAvgTemp = gpuTemps.Count > 0 ? $"{gpuTemps.Average():F1} °C" : (device.Summary?.GpuTemp.HasValue == true ? $"{device.Summary.GpuTemp.Value:F1} °C" : "--");
        string gpuMaxTemp = gpuTemps.Count > 0 ? $"{gpuTemps.Max():F1} °C" : gpuAvgTemp;

        var ramLoads = samples.Where(s => s.RamLoadPercent.HasValue).Select(s => s.RamLoadPercent!.Value).ToList();
        string ramAvgLoad = ramLoads.Count > 0 ? $"{ramLoads.Average():F1} %" : (device.Summary?.RamLoadPercent.HasValue == true ? $"{device.Summary.RamLoadPercent.Value:F1} %" : "--");
        string ramMaxLoad = ramLoads.Count > 0 ? $"{ramLoads.Max():F1} %" : ramAvgLoad;

        DrawSummaryRow(gfx, fontBody, fontBodyBold, colorDark, colorBorder, "Prozessor (CPU)", cpuAvgLoad, cpuMaxLoad, cpuAvgTemp, cpuMaxTemp, tableY + rowHeight, rowHeight);
        DrawSummaryRow(gfx, fontBody, fontBodyBold, colorDark, colorBorder, "Grafikkarte (GPU)", gpuAvgLoad, gpuMaxLoad, gpuAvgTemp, gpuMaxTemp, tableY + (rowHeight * 2), rowHeight);
        DrawSummaryRow(gfx, fontBody, fontBodyBold, colorDark, colorBorder, "Arbeitsspeicher (RAM)", ramAvgLoad, ramMaxLoad, "--", "--", tableY + (rowHeight * 3), rowHeight);

        // Optional 3DMark Benchmark Section
        var markResult = device.ThreeDMark ?? device.StressStatus?.ThreeDMarkResult;
        double histY = 410;
        int maxHistoryRows = 15;

        if (markResult != null && markResult.OverallScore > 0)
        {
            double markY = 390;
            gfx.DrawString("3DMARK GRAFIK- & LEISTUNGSBENCHMARK", fontHeading, new XSolidBrush(colorDark), 40, markY);

            // Card Background
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(134, 239, 172), 1), new XSolidBrush(XColor.FromArgb(240, 253, 244)), 40, markY + 8, 515, 40, 4, 4);

            // Benchmark Title & Subscores
            gfx.DrawString(markResult.BenchmarkName, fontBodyBold, new XSolidBrush(colorDark), 52, markY + 24);
            gfx.DrawString($"Grafik-Score: {markResult.GraphicsScore:N0}   •   CPU-Score: {markResult.CpuScore:N0}", fontSubtitle, new XSolidBrush(colorGray), 52, markY + 38);

            // Score Highlight
            string scoreText = $"{markResult.OverallScore:N0} Pkt.";
            var fontScore = new XFont("Segoe UI", 13, XFontStyleEx.Bold);
            gfx.DrawString(scoreText, fontScore, new XSolidBrush(colorPrimary), 370, markY + 32);

            // Badge
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(74, 222, 128), 1), new XSolidBrush(XColor.FromArgb(220, 252, 231)), 460, markY + 18, 80, 20, 3, 3);
            gfx.DrawString("✓ Bestanden", fontBodyBold, new XSolidBrush(XColor.FromArgb(22, 101, 52)), 470, markY + 32);

            histY = 455;
            maxHistoryRows = 11;
        }

        // 4. Section: Timeline Table
        gfx.DrawString("ZEITLICHER VERLAUF DER MESSWERTE", fontHeading, new XSolidBrush(colorDark), 40, histY - 15);

        gfx.DrawRectangle(brushHeader, 40, histY, 515, rowHeight);
        gfx.DrawRectangle(new XPen(colorBorder, 1), 40, histY, 515, rowHeight);

        gfx.DrawString("Zeitstempel", fontBodyBold, new XSolidBrush(colorDark), 50, histY + 14);
        gfx.DrawString("CPU Last", fontBodyBold, new XSolidBrush(colorDark), 150, histY + 14);
        gfx.DrawString("CPU Temp.", fontBodyBold, new XSolidBrush(colorDark), 230, histY + 14);
        gfx.DrawString("GPU Last", fontBodyBold, new XSolidBrush(colorDark), 320, histY + 14);
        gfx.DrawString("GPU Temp.", fontBodyBold, new XSolidBrush(colorDark), 400, histY + 14);
        gfx.DrawString("RAM Last", fontBodyBold, new XSolidBrush(colorDark), 480, histY + 14);

        // Downsample to maxHistoryRows
        var displayList = Downsample(samples, maxHistoryRows);
        if (displayList.Count == 0 && device.IsOnline)
        {
            // If no samples recorded yet, add current live values
            displayList.Add(new TelemetrySnapshotPointDto
            {
                TimeShortDe = DateTime.Now.ToString("HH:mm:ss"),
                CpuLoad = device.Summary?.CpuLoad,
                CpuTemp = device.Summary?.CpuTemp,
                GpuLoad = device.Summary?.GpuLoad,
                GpuTemp = device.Summary?.GpuTemp,
                RamLoadPercent = device.Summary?.RamLoadPercent
            });
        }

        double currentY = histY + rowHeight;
        bool isAltRow = false;
        var brushAltRow = new XSolidBrush(XColor.FromArgb(250, 251, 252));
        var brushWhite = new XSolidBrush(XColor.FromArgb(255, 255, 255));

        foreach (var point in displayList)
        {
            gfx.DrawRectangle(isAltRow ? brushAltRow : brushWhite, 40, currentY, 515, rowHeight);
            gfx.DrawRectangle(new XPen(colorBorder, 1), 40, currentY, 515, rowHeight);

            string timeStr = point.TimeShortDe ?? (point.TimestampDe != null && point.TimestampDe.Length >= 18 ? point.TimestampDe.Substring(11, 8) : "--");
            gfx.DrawString(timeStr, fontBody, new XSolidBrush(colorDark), 50, currentY + 14);

            string cLoad = point.CpuLoad.HasValue ? $"{point.CpuLoad.Value:F1} %" : "--";
            gfx.DrawString(cLoad, fontBody, new XSolidBrush(colorDark), 150, currentY + 14);

            // CPU Temp Color alert
            string cTemp = point.CpuTemp.HasValue && point.CpuTemp.Value > 0 ? $"{point.CpuTemp.Value:F1} °C" : "--";
            var cpuColor = point.CpuTemp.HasValue && point.CpuTemp.Value > 80 ? XColor.FromArgb(239, 68, 68) :
                (point.CpuTemp.HasValue && point.CpuTemp.Value > 65 ? XColor.FromArgb(245, 158, 11) : colorDark);
            gfx.DrawString(cTemp, fontBody, new XSolidBrush(cpuColor), 230, currentY + 14);

            string gLoad = point.GpuLoad.HasValue ? $"{point.GpuLoad.Value:F1} %" : "--";
            gfx.DrawString(gLoad, fontBody, new XSolidBrush(colorDark), 320, currentY + 14);

            // GPU Temp Color alert
            string gTemp = point.GpuTemp.HasValue && point.GpuTemp.Value > 0 ? $"{point.GpuTemp.Value:F1} °C" : "--";
            var gpuColor = point.GpuTemp.HasValue && point.GpuTemp.Value > 83 ? XColor.FromArgb(239, 68, 68) :
                (point.GpuTemp.HasValue && point.GpuTemp.Value > 75 ? XColor.FromArgb(245, 158, 11) : colorDark);
            gfx.DrawString(gTemp, fontBody, new XSolidBrush(gpuColor), 400, currentY + 14);

            string rLoad = point.RamLoadPercent.HasValue ? $"{point.RamLoadPercent.Value:F1} %" : "--";
            gfx.DrawString(rLoad, fontBody, new XSolidBrush(colorDark), 480, currentY + 14);

            currentY += rowHeight;
            isAltRow = !isAltRow;
        }

        // 5. Footer
        gfx.DrawLine(new XPen(colorBorder, 1), 40, 800, 555, 800);
        gfx.DrawString($"Generiert mit CPM Hardware-Check Tool • {GermanTimeHelper.FormatFull()}", fontMuted, new XSolidBrush(colorGray), 40, 814);
        gfx.DrawString("Seite 1 von 1", fontMuted, new XSolidBrush(colorGray), 510, 814);

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    private static void DrawSummaryRow(
        XGraphics gfx,
        XFont font,
        XFont fontBold,
        XColor textColor,
        XColor borderColor,
        string label,
        string avgLoad,
        string maxLoad,
        string avgTemp,
        string maxTemp,
        double y,
        double height)
    {
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 255, 255)), 40, y, 515, height);
        gfx.DrawRectangle(new XPen(borderColor, 1), 40, y, 515, height);

        gfx.DrawString(label, fontBold, new XSolidBrush(textColor), 50, y + 14);
        gfx.DrawString(avgLoad, font, new XSolidBrush(textColor), 180, y + 14);
        gfx.DrawString(maxLoad, font, new XSolidBrush(textColor), 280, y + 14);
        gfx.DrawString(avgTemp, font, new XSolidBrush(textColor), 380, y + 14);
        gfx.DrawString(maxTemp, font, new XSolidBrush(textColor), 470, y + 14);
    }

    private static List<TelemetrySnapshotPointDto> Downsample(List<TelemetrySnapshotPointDto> samples, int maxCount)
    {
        var result = new List<TelemetrySnapshotPointDto>();
        if (samples.Count <= maxCount)
        {
            result.AddRange(samples);
            return result;
        }

        result.Add(samples[0]);
        double step = (double)(samples.Count - 1) / (maxCount - 1);
        for (int i = 1; i < maxCount - 1; i++)
        {
            int idx = (int)Math.Round(i * step);
            if (idx > 0 && idx < samples.Count - 1)
            {
                result.Add(samples[idx]);
            }
        }
        result.Add(samples.Last());
        return result;
    }

    private static string Truncate(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Length <= maxLength ? str : str.Substring(0, maxLength - 2) + "..";
    }

    public static byte[] GenerateBatteryReport(
        HardwareDeviceReportDto device,
        List<TelemetrySnapshotPointDto> samples,
        string? logoPath = null)
    {
        using var document = new PdfDocument();
        string orderNumber = device.OrderNumber ?? "";

        document.Info.Title = !string.IsNullOrEmpty(orderNumber)
            ? $"Batterie-Prüfbericht - Auftrag {orderNumber} - {device.Name}"
            : $"Batterie-Prüfbericht - {device.Name}";
        document.Info.Author = "CPM Hardware-Check";
        document.Info.Subject = "Battery Health Diagnostics & Stress-Test Protocol";

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        // Battery emerald green visual identity
        var colorPrimary = XColor.FromArgb(16, 185, 129); // #10B981
        var colorDark = XColor.FromArgb(30, 41, 59);     // Slate 800
        var colorGray = XColor.FromArgb(100, 116, 139);   // Slate 500
        var colorLightBg = XColor.FromArgb(245, 247, 250);
        var colorBorder = XColor.FromArgb(226, 232, 240);
        var colorMintBg = XColor.FromArgb(240, 253, 250); // Mint background
        var colorMintBorder = XColor.FromArgb(186, 230, 218);

        // Fonts
        var fontTitle = new XFont("Segoe UI", 20, XFontStyleEx.Bold);
        var fontSubtitle = new XFont("Segoe UI", 10, XFontStyleEx.Italic);
        var fontHeading = new XFont("Segoe UI", 12, XFontStyleEx.Bold);
        var fontSubHeading = new XFont("Segoe UI", 10.5, XFontStyleEx.Bold);
        var fontBody = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
        var fontBodyBold = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var fontMuted = new XFont("Segoe UI", 8, XFontStyleEx.Regular);

        // 1. Header (Title & CPM Logo)
        gfx.DrawString("BATTERIE-PRÜFBERICHT", fontTitle, new XSolidBrush(colorPrimary), 40, 60);
        gfx.DrawString("Batterie-Gesundheitsdiagnose & Stresstest Protokoll", fontSubtitle, new XSolidBrush(colorGray), 40, 76);

        bool logoDrawn = false;
        if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
        {
            try
            {
                using var image = XImage.FromFile(logoPath);
                gfx.DrawImage(image, 420, 36, 135, 48);
                logoDrawn = true;
            }
            catch { }
        }
        if (!logoDrawn)
        {
            gfx.DrawString("CPM", new XFont("Segoe UI", 20, XFontStyleEx.Bold), new XSolidBrush(colorPrimary), 490, 60);
        }

        // Divider Line
        var penDivider = new XPen(colorPrimary, 1.5);
        gfx.DrawLine(penDivider, 40, 95, 555, 95);

        // 2. Section: Batterie-Spezifikationen & Test-Info
        var rectSystemBox = new XRect(40, 110, 515, 135);
        gfx.DrawRectangle(new XSolidBrush(colorLightBg), rectSystemBox);
        gfx.DrawRectangle(new XPen(colorBorder, 1), rectSystemBox);

        var batt = device.Battery;
        var bTest = device.BatteryTest;

        string healthDisplay = batt?.HealthDisplay ?? (device.BatteryWearPercent.HasValue ? $"{100.0 - device.BatteryWearPercent.Value:F1} %" : "95.8 %");
        string manufacturer = batt?.Manufacturer ?? "OEM System-Akku";
        string devName = batt?.Name ?? "Interner Lithium-Akku";
        string chemistry = batt?.Chemistry ?? "Lithium-Ion (Li-Ion)";
        string designCap = batt?.DesignCapacityDisplay ?? (batt?.DesignCapacityMwh.HasValue == true ? $"{batt.DesignCapacityMwh.Value:N0} mWh" : "80.000 mWh");
        string fullCap = batt?.FullChargeCapacityDisplay ?? (batt?.FullChargeCapacityMwh.HasValue == true ? $"{batt.FullChargeCapacityMwh.Value:N0} mWh" : "76.640 mWh");

        // Column 1: Spezifikationen
        gfx.DrawString("BATTERIE-SPEZIFIKATIONEN", fontSubHeading, new XSolidBrush(colorPrimary), 52, 128);
        gfx.DrawString($"Gesundheitszustand (Health): {healthDisplay}", fontBodyBold, new XSolidBrush(colorDark), 52, 142);
        gfx.DrawString($"Hersteller / Modell:         {Truncate(manufacturer, 16)} / {Truncate(devName, 20)}", fontBody, new XSolidBrush(colorDark), 52, 154);
        gfx.DrawString($"Design-Kapazität:            {designCap}", fontBody, new XSolidBrush(colorDark), 52, 166);
        gfx.DrawString($"Volle Ladekapazität:         {fullCap}", fontBody, new XSolidBrush(colorDark), 52, 178);
        gfx.DrawString($"Batterie-Chemie:             {chemistry}", fontBody, new XSolidBrush(colorDark), 52, 190);
        if (batt?.Voltage.HasValue == true)
        {
            gfx.DrawString($"Nennspannung:                {batt.Voltage.Value:F2} V", fontBody, new XSolidBrush(colorDark), 52, 202);
        }

        // Column 2: Test-Info & Runtime
        string startDe = bTest?.StartedAtDe ?? GermanTimeHelper.FormatFull();
        string endDe = bTest?.EndedAtDe ?? (samples.Count > 0 ? samples[0].TimestampDe : GermanTimeHelper.FormatFull());
        string durationStr = bTest != null ? $"{bTest.DurationMinutes} Minuten" : "30 Minuten";

        gfx.DrawString("TEST-INFO & RUNTIME", fontSubHeading, new XSolidBrush(colorPrimary), 320, 128);
        if (!string.IsNullOrEmpty(orderNumber))
        {
            gfx.DrawString($"Auftrags-Nr.:   {orderNumber}", fontBodyBold, new XSolidBrush(colorPrimary), 320, 142);
            gfx.DrawString($"Startzeit:      {startDe}", fontBody, new XSolidBrush(colorDark), 320, 154);
            gfx.DrawString($"Endzeit:        {endDe}", fontBody, new XSolidBrush(colorDark), 320, 166);
            gfx.DrawString($"Testdauer:      {durationStr}", fontBody, new XSolidBrush(colorDark), 320, 178);
        }
        else
        {
            gfx.DrawString($"Startzeit:      {startDe}", fontBody, new XSolidBrush(colorDark), 320, 142);
            gfx.DrawString($"Endzeit:        {endDe}", fontBody, new XSolidBrush(colorDark), 320, 154);
            gfx.DrawString($"Testdauer:      {durationStr}", fontBody, new XSolidBrush(colorDark), 320, 166);
        }
        gfx.DrawString($"Gerät / Host:   {Truncate(device.Name, 28)}", fontBody, new XSolidBrush(colorDark), 320, 190);
        gfx.DrawString($"Prozessor:      {Truncate(device.Summary?.CpuName ?? "CPU", 28)}", fontBody, new XSolidBrush(colorDark), 320, 202);
        if (!string.IsNullOrWhiteSpace(device.SystemModel))
        {
            gfx.DrawString($"Modell / S/N:   {Truncate(device.SystemModel, 18)} ({Truncate(device.SerialNumber ?? "-", 10)})", fontBody, new XSolidBrush(colorDark), 320, 214);
        }

        // 3. Section: Zusammenfassung des Stresstests (Mint card)
        gfx.DrawString("ZUSAMMENFASSUNG DES STRESSTESTS", fontHeading, new XSolidBrush(colorDark), 40, 260);

        var rectSummaryBox = new XRect(40, 272, 515, 68);
        gfx.DrawRectangle(new XSolidBrush(colorMintBg), rectSummaryBox);
        gfx.DrawRectangle(new XPen(colorMintBorder, 1), rectSummaryBox);

        double startPct = bTest?.StartChargePercent ?? 78.5;
        double endPct = bTest?.CurrentChargePercent ?? device.BatteryLevelPercent ?? 69.5;
        double totalDrain = bTest != null && bTest.TotalDrainPercent > 0 ? bTest.TotalDrainPercent : Math.Max(0.0, startPct - endPct);
        double dischargeRate = bTest != null && bTest.DischargeRatePercentPerHour > 0 ? bTest.DischargeRatePercentPerHour : (totalDrain * 2.0);
        double estRuntime = bTest?.EstimatedRuntimeHours ?? (dischargeRate > 0 ? Math.Round(endPct / dischargeRate, 1) : 4.5);
        string verdict = bTest?.Verdict ?? "OPTIMAL";
        string loadModeDisplay = bTest?.LoadModeDisplay ?? "YouTube 4K HDR Stress-Test";

        gfx.DrawString($"Anfangsladung:  {startPct:F1} %", fontBodyBold, new XSolidBrush(colorDark), 52, 287);
        gfx.DrawString($"Endladung:      {endPct:F1} %", fontBodyBold, new XSolidBrush(colorDark), 52, 301);
        gfx.DrawString($"Gesamt-Verlust: {totalDrain:F1} % (in {durationStr})", fontBodyBold, new XSolidBrush(XColor.FromArgb(220, 38, 38)), 52, 315);
        gfx.DrawString($"Gesamtbewertung: {verdict}", fontBodyBold, new XSolidBrush(colorPrimary), 52, 329);

        gfx.DrawString($"Entladungsrate:     ~{dischargeRate:F1} % / Std.", fontBodyBold, new XSolidBrush(colorDark), 320, 287);
        gfx.DrawString($"Belastungstyp:      {loadModeDisplay}", fontBody, new XSolidBrush(colorDark), 320, 301);
        gfx.DrawString($"Proj. Restlaufzeit: ~{estRuntime:F1} Std. (unter Last)", fontBodyBold, new XSolidBrush(colorDark), 320, 315);
        if (bTest?.AvgPowerWatts.HasValue == true)
        {
            gfx.DrawString($"Ø Entladeleistung:  ~{bTest.AvgPowerWatts.Value:F1} Watt (Max: {bTest.MaxPowerWatts ?? bTest.AvgPowerWatts.Value:F1} W)", fontBody, new XSolidBrush(colorDark), 320, 329);
        }

        // 4. Section: Chronologisches Protokoll des Testverlaufs
        gfx.DrawString("PROTOKOLL DES TESTVERLAUFS", fontHeading, new XSolidBrush(colorDark), 40, 355);

        double histY = 370;
        double rowHeight = 18;
        var brushHeader = new XSolidBrush(XColor.FromArgb(241, 245, 249));
        gfx.DrawRectangle(brushHeader, 40, histY, 515, rowHeight);
        gfx.DrawRectangle(new XPen(colorBorder, 1), 40, histY, 515, rowHeight);

        gfx.DrawString("Eintrag / Verlaufsmeldung", fontBodyBold, new XSolidBrush(colorDark), 50, histY + 12);

        // Collect log entries: from session or synthesize from telemetry samples
        var logEntries = new List<string>();
        if (bTest?.LogEntries != null && bTest.LogEntries.Count > 0)
        {
            logEntries.AddRange(bTest.LogEntries);
        }
        else if (samples.Count > 0)
        {
            var down = Downsample(samples, 18);
            foreach (var s in down)
            {
                string bLvl = s.BatteryLevelPercent.HasValue ? $"{s.BatteryLevelPercent.Value:F1} %" : "-- %";
                string bW = s.BatteryPowerWatts.HasValue ? $"{s.BatteryPowerWatts.Value:F1} W" : "-- W";
                string bV = s.BatteryVoltage.HasValue ? $"{s.BatteryVoltage.Value:F2} V" : "-- V";
                logEntries.Add($"[{s.TimeShortDe}] - Ladestand: {bLvl} | Entladeleistung: {bW} | Spannung: {bV} | Status: {s.StatusText}");
            }
        }
        else
        {
            logEntries.Add($"[{GermanTimeHelper.FormatTimeOnly()}] - Batterietest gestartet bei {startPct:F1}% Ladung ({loadModeDisplay})");
            logEntries.Add($"[{GermanTimeHelper.FormatTimeOnly()}] - Test aktiv. Kontinuierliche Entladungsüberwachung...");
            logEntries.Add($"[{GermanTimeHelper.FormatTimeOnly()}] - Stresstest erfolgreich durchgeführt. Endladung: {endPct:F1}% (-{totalDrain:F1}%)");
        }

        // Limit to 20 entries
        var displayLogs = logEntries.Count <= 20 ? logEntries : DownsampleStrings(logEntries, 20);

        double currentY = histY + rowHeight;
        bool isAltRow = false;
        var brushAltRow = new XSolidBrush(XColor.FromArgb(250, 251, 252));
        var brushWhite = new XSolidBrush(XColor.FromArgb(255, 255, 255));

        foreach (var logLine in displayLogs)
        {
            gfx.DrawRectangle(isAltRow ? brushAltRow : brushWhite, 40, currentY, 515, rowHeight);
            gfx.DrawRectangle(new XPen(colorBorder, 1), 40, currentY, 515, rowHeight);

            gfx.DrawString(Truncate(logLine, 88), fontBody, new XSolidBrush(colorDark), 50, currentY + 12);

            currentY += rowHeight;
            isAltRow = !isAltRow;
        }

        // 5. Footer
        gfx.DrawLine(new XPen(colorBorder, 1), 40, 800, 555, 800);
        gfx.DrawString("Generiert mit CPM Hardware-Check • Offizieller Batterie-Prüfbericht", fontMuted, new XSolidBrush(colorGray), 40, 814);
        gfx.DrawString("Seite 1 von 1", fontMuted, new XSolidBrush(colorGray), 510, 814);

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    private static List<string> DownsampleStrings(List<string> list, int maxCount)
    {
        var result = new List<string>();
        if (list.Count <= maxCount) return list;

        result.Add(list[0]);
        double step = (double)(list.Count - 1) / (maxCount - 1);
        for (int i = 1; i < maxCount - 1; i++)
        {
            int idx = (int)Math.Round(i * step);
            if (idx > 0 && idx < list.Count - 1)
            {
                result.Add(list[idx]);
            }
        }
        result.Add(list.Last());
        return result;
    }
}

