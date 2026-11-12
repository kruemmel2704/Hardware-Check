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
        PdfFontResolver.EnsureRegistered();

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

        // 4. Section: Temperatur-Diagramm
        gfx.DrawString("TEMPERATURVERLAUF", fontHeading, new XSolidBrush(colorDark), 40, 392);
        PdfChartRenderer.Draw(gfx, new XRect(40, 400, 515, 205), samples, new[]
        {
            new PdfChartSeries { Label = "CPU Package", Unit = "°C", Color = XColor.FromArgb(0, 159, 227), Selector = s => s.CpuTemp, Fill = true },
            new PdfChartSeries { Label = "CPU Max Core", Unit = "°C", Color = XColor.FromArgb(99, 102, 241), Selector = s => s.CpuMaxCoreTemp },
            new PdfChartSeries { Label = "GPU Core", Unit = "°C", Color = XColor.FromArgb(16, 185, 129), Selector = s => s.GpuTemp },
            new PdfChartSeries { Label = "GPU Hot Spot", Unit = "°C", Color = XColor.FromArgb(245, 158, 11), Selector = s => s.GpuHotSpotTemp },
            new PdfChartSeries { Label = "Mainboard", Unit = "°C", Color = XColor.FromArgb(139, 92, 246), Selector = s => s.MainboardTemp },
            new PdfChartSeries { Label = "SSD / NVMe", Unit = "°C", Color = XColor.FromArgb(236, 72, 153), Selector = s => s.StorageTemp },
        }, new PdfChartOptions
        {
            Title = "Temperaturen von CPU, GPU, Mainboard und SSD im Zeitverlauf",
            LeftAxisUnit = "°C",
            ThresholdValue = 85,
            ThresholdLabel = "85 °C Grenze",
            EmptyText = "Keine Temperatur-Messwerte aufgezeichnet."
        });

        // 5. Section: Timeline Table (wird bei Bedarf auf Seite 2 fortgesetzt)
        double histY = 640;
        int maxHistoryRows = 15;
        double pageBottom = 790;

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

        int rowsOnFirstPage = (int)Math.Floor((pageBottom - (histY + rowHeight)) / rowHeight);
        int totalPages = displayList.Count > rowsOnFirstPage ? 2 : 1;

        void DrawTimelineHeader(XGraphics g, double y, bool continued)
        {
            g.DrawString(continued ? "ZEITLICHER VERLAUF DER MESSWERTE (FORTSETZUNG)" : "ZEITLICHER VERLAUF DER MESSWERTE", fontHeading, new XSolidBrush(colorDark), 40, y - 15);
            g.DrawRectangle(brushHeader, 40, y, 515, rowHeight);
            g.DrawRectangle(new XPen(colorBorder, 1), 40, y, 515, rowHeight);
            g.DrawString("Zeitstempel", fontBodyBold, new XSolidBrush(colorDark), 50, y + 14);
            g.DrawString("CPU Last", fontBodyBold, new XSolidBrush(colorDark), 150, y + 14);
            g.DrawString("CPU Temp.", fontBodyBold, new XSolidBrush(colorDark), 230, y + 14);
            g.DrawString("GPU Last", fontBodyBold, new XSolidBrush(colorDark), 320, y + 14);
            g.DrawString("GPU Temp.", fontBodyBold, new XSolidBrush(colorDark), 400, y + 14);
            g.DrawString("RAM Last", fontBodyBold, new XSolidBrush(colorDark), 480, y + 14);
        }

        void DrawFooter(XGraphics g, int pageNo)
        {
            g.DrawLine(new XPen(colorBorder, 1), 40, 800, 555, 800);
            g.DrawString($"Generiert mit CPM Hardware-Check Tool • {GermanTimeHelper.FormatFull()}", fontMuted, new XSolidBrush(colorGray), 40, 814);
            g.DrawString($"Seite {pageNo} von {totalPages}", fontMuted, new XSolidBrush(colorGray), 510, 814);
        }

        DrawTimelineHeader(gfx, histY, false);

        double currentY = histY + rowHeight;
        bool isAltRow = false;
        int pageNo = 1;
        var brushAltRow = new XSolidBrush(XColor.FromArgb(250, 251, 252));
        var brushWhite = new XSolidBrush(XColor.FromArgb(255, 255, 255));

        foreach (var point in displayList)
        {
            if (currentY + rowHeight > pageBottom)
            {
                DrawFooter(gfx, pageNo);
                gfx.Dispose();
                var nextPage = document.AddPage();
                nextPage.Size = PdfSharp.PageSize.A4;
                gfx = XGraphics.FromPdfPage(nextPage);
                pageNo++;
                DrawTimelineHeader(gfx, 60, true);
                currentY = 60 + rowHeight;
            }

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

        // 6. Footer
        DrawFooter(gfx, pageNo);
        gfx.Dispose();

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
        PdfFontResolver.EnsureRegistered();

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

        // Nur echte Messwerte – fehlende Angaben erscheinen als "--" (keine Platzhalterzahlen)
        var battSamples = samples.Where(s => s.BatteryLevelPercent.HasValue).ToList();
        string healthDisplay = batt?.HealthPercent.HasValue == true
            ? batt.HealthDisplay
            : (device.BatteryWearPercent.HasValue ? $"{100.0 - device.BatteryWearPercent.Value:F1} %" : "--");
        string manufacturer = string.IsNullOrWhiteSpace(batt?.Manufacturer) ? "--" : batt!.Manufacturer;
        string devName = string.IsNullOrWhiteSpace(batt?.Name) ? "--" : batt!.Name;
        string chemistry = string.IsNullOrWhiteSpace(batt?.Chemistry) ? "--" : batt!.Chemistry;
        string designCap = batt?.DesignCapacityMwh.HasValue == true ? $"{batt.DesignCapacityMwh.Value:N0} mWh" : "--";
        string fullCap = batt?.FullChargeCapacityMwh.HasValue == true ? $"{batt.FullChargeCapacityMwh.Value:N0} mWh" : "--";

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
        string startDe = bTest?.StartedAtDe ?? battSamples.FirstOrDefault()?.TimestampDe ?? "--";
        string endDe = bTest?.EndedAtDe ?? battSamples.LastOrDefault()?.TimestampDe ?? "--";
        // Tatsächlich gemessene Dauer (bei laufendem Test die bisher vergangene Zeit)
        double? durationMinutes = bTest != null
            ? (bTest.ElapsedSeconds > 0 ? Math.Round(bTest.ElapsedSeconds / 60.0, 1) : bTest.DurationMinutes)
            : MinutesBetween(battSamples.FirstOrDefault()?.TimestampDe, battSamples.LastOrDefault()?.TimestampDe);
        string durationStr = !durationMinutes.HasValue
            ? "--"
            : bTest != null && bTest.IsRunning
                ? $"{durationMinutes.Value:F0} von {bTest.DurationMinutes} Minuten (läuft)"
                : $"{durationMinutes.Value:F0} Minuten";

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

        double? startPct = bTest?.StartChargePercent ?? battSamples.FirstOrDefault()?.BatteryLevelPercent;
        double? endPct = bTest?.CurrentChargePercent ?? battSamples.LastOrDefault()?.BatteryLevelPercent ?? device.BatteryLevelPercent;
        double? totalDrain = bTest != null && bTest.TotalDrainPercent > 0
            ? bTest.TotalDrainPercent
            : (startPct.HasValue && endPct.HasValue ? Math.Max(0.0, startPct.Value - endPct.Value) : null);
        double dischargeRate = bTest != null && bTest.DischargeRatePercentPerHour > 0
            ? bTest.DischargeRatePercentPerHour
            : (totalDrain.HasValue && durationMinutes is > 0 ? totalDrain.Value * 60.0 / durationMinutes.Value : 0);
        // Ohne messbare Entladung keine erfundene Restlaufzeit ausgeben
        double? estRuntime = bTest?.EstimatedRuntimeHours is double est && est > 0
            ? est
            : (dischargeRate > 0 && endPct.HasValue ? Math.Round(endPct.Value / dischargeRate, 1) : (double?)null);
        string verdict = bTest?.Verdict ?? "nicht bewertet";
        string loadModeDisplay = bTest?.LoadModeDisplay ?? "--";
        string Pct(double? v) => v.HasValue ? $"{v.Value:F1} %" : "--";

        gfx.DrawString($"Anfangsladung:  {Pct(startPct)}", fontBodyBold, new XSolidBrush(colorDark), 52, 287);
        gfx.DrawString($"Endladung:      {Pct(endPct)}", fontBodyBold, new XSolidBrush(colorDark), 52, 301);
        gfx.DrawString($"Gesamt-Verlust: {Pct(totalDrain)}{(durationMinutes.HasValue ? $" (in {durationMinutes.Value:F0} Min.)" : "")}", fontBodyBold, new XSolidBrush(XColor.FromArgb(220, 38, 38)), 52, 315);
        gfx.DrawString($"Gesamtbewertung: {verdict}", fontBodyBold, new XSolidBrush(colorPrimary), 52, 329);

        gfx.DrawString(dischargeRate > 0 ? $"Entladungsrate:     ca. {dischargeRate:F1} % / Std." : "Entladungsrate:     keine Entladung gemessen", fontBodyBold, new XSolidBrush(colorDark), 320, 287);
        gfx.DrawString($"Belastungstyp:      {loadModeDisplay}", fontBody, new XSolidBrush(colorDark), 320, 301);
        gfx.DrawString(estRuntime.HasValue ? $"Proj. Restlaufzeit: ca. {estRuntime.Value:F1} Std. (unter Last)" : "Proj. Restlaufzeit: -- (zu wenig Entladung)", fontBodyBold, new XSolidBrush(colorDark), 320, 315);
        if (bTest?.AvgPowerWatts.HasValue == true)
        {
            gfx.DrawString($"Ø Entladeleistung:  ~{bTest.AvgPowerWatts.Value:F1} Watt (Max: {bTest.MaxPowerWatts ?? bTest.AvgPowerWatts.Value:F1} W)", fontBody, new XSolidBrush(colorDark), 320, 329);
        }

        // 4. Section: Batterie-Diagramm (Ladestand links, Leistung rechts)
        gfx.DrawString("BATTERIEVERLAUF", fontHeading, new XSolidBrush(colorDark), 40, 360);
        PdfChartRenderer.Draw(gfx, new XRect(40, 368, 515, 200), battSamples.Count > 0 ? battSamples : samples, new[]
        {
            new PdfChartSeries { Label = "Ladestand", Unit = "%", Color = XColor.FromArgb(16, 185, 129), Selector = s => s.BatteryLevelPercent, Fill = true },
            new PdfChartSeries { Label = "Entladeleistung", Unit = "W", Color = XColor.FromArgb(245, 158, 11), Selector = s => s.BatteryPowerWatts, RightAxis = true },
        }, new PdfChartOptions
        {
            Title = "Ladestand (%) und Entladeleistung (W) im Testverlauf",
            LeftAxisUnit = "%",
            RightAxisUnit = "W",
            LeftMin = 0,
            LeftMax = 100,
            RightMin = 0,
            EmptyText = "Keine Akku-Messwerte aufgezeichnet."
        });

        // 5. Section: Chronologisches Protokoll des Testverlaufs (bei Bedarf auf Seite 2)
        double histY = 605;
        double rowHeight = 18;
        double pageBottom = 790;
        var brushHeader = new XSolidBrush(XColor.FromArgb(241, 245, 249));

        // Protokoll: Testsitzung > echte Messpunkte > Hinweis (keine erfundenen Einträge)
        var logEntries = new List<string>();
        if (bTest?.LogEntries != null && bTest.LogEntries.Count > 0)
        {
            logEntries.AddRange(bTest.LogEntries);
        }
        else if (battSamples.Count > 0)
        {
            var down = Downsample(battSamples, 18);
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
            logEntries.Add("Für dieses Gerät liegen keine Akku-Messwerte vor.");
        }

        // Limit to 20 entries
        var displayLogs = logEntries.Count <= 20 ? logEntries : DownsampleStrings(logEntries, 20);

        int rowsOnFirstPage = (int)Math.Floor((pageBottom - (histY + rowHeight)) / rowHeight);
        int totalPages = displayLogs.Count > rowsOnFirstPage ? 2 : 1;

        void DrawLogHeader(XGraphics g, double y, bool continued)
        {
            g.DrawString(continued ? "PROTOKOLL DES TESTVERLAUFS (FORTSETZUNG)" : "PROTOKOLL DES TESTVERLAUFS", fontHeading, new XSolidBrush(colorDark), 40, y - 15);
            g.DrawRectangle(brushHeader, 40, y, 515, rowHeight);
            g.DrawRectangle(new XPen(colorBorder, 1), 40, y, 515, rowHeight);
            g.DrawString("Eintrag / Verlaufsmeldung", fontBodyBold, new XSolidBrush(colorDark), 50, y + 12);
        }

        void DrawFooter(XGraphics g, int pageNo)
        {
            g.DrawLine(new XPen(colorBorder, 1), 40, 800, 555, 800);
            g.DrawString("Generiert mit CPM Hardware-Check • Offizieller Batterie-Prüfbericht", fontMuted, new XSolidBrush(colorGray), 40, 814);
            g.DrawString($"Seite {pageNo} von {totalPages}", fontMuted, new XSolidBrush(colorGray), 510, 814);
        }

        DrawLogHeader(gfx, histY, false);

        double currentY = histY + rowHeight;
        bool isAltRow = false;
        int pageNo = 1;
        var brushAltRow = new XSolidBrush(XColor.FromArgb(250, 251, 252));
        var brushWhite = new XSolidBrush(XColor.FromArgb(255, 255, 255));

        foreach (var logLine in displayLogs)
        {
            if (currentY + rowHeight > pageBottom)
            {
                DrawFooter(gfx, pageNo);
                gfx.Dispose();
                var nextPage = document.AddPage();
                nextPage.Size = PdfSharp.PageSize.A4;
                gfx = XGraphics.FromPdfPage(nextPage);
                pageNo++;
                DrawLogHeader(gfx, 60, true);
                currentY = 60 + rowHeight;
            }

            gfx.DrawRectangle(isAltRow ? brushAltRow : brushWhite, 40, currentY, 515, rowHeight);
            gfx.DrawRectangle(new XPen(colorBorder, 1), 40, currentY, 515, rowHeight);

            gfx.DrawString(Truncate(logLine, 88), fontBody, new XSolidBrush(colorDark), 50, currentY + 12);

            currentY += rowHeight;
            isAltRow = !isAltRow;
        }

        // 6. Footer
        DrawFooter(gfx, pageNo);
        gfx.Dispose();

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    private static double? MinutesBetween(string? startDe, string? endDe)
    {
        static DateTime? Parse(string? t)
        {
            if (string.IsNullOrWhiteSpace(t) || t.Length < 19) return null;
            return DateTime.TryParseExact(t.Substring(0, 19), "dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        }
        var a = Parse(startDe);
        var b = Parse(endDe);
        return a.HasValue && b.HasValue && b > a ? Math.Round((b.Value - a.Value).TotalMinutes) : null;
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

