using HardwareCheck.Api.Models;
using PdfSharp.Drawing;

namespace HardwareCheck.Api.Services;

/// <summary>Eine Messreihe im PDF-Diagramm.</summary>
public sealed class PdfChartSeries
{
    public string Label { get; init; } = "";
    public string Unit { get; init; } = "";
    public XColor Color { get; init; }
    public Func<TelemetrySnapshotPointDto, double?> Selector { get; init; } = _ => null;
    /// <summary>Werte an der rechten Y-Achse (z.B. Watt neben Prozent).</summary>
    public bool RightAxis { get; init; }
    public bool Fill { get; init; }
}

public sealed class PdfChartOptions
{
    public string Title { get; init; } = "";
    public string LeftAxisUnit { get; init; } = "";
    public string RightAxisUnit { get; init; } = "";
    public double? LeftMin { get; init; }
    public double? LeftMax { get; init; }
    public double? RightMin { get; init; }
    public double? RightMax { get; init; }
    public double? ThresholdValue { get; init; }
    public string? ThresholdLabel { get; init; }
    public string EmptyText { get; init; } = "Keine Messwerte vorhanden.";
}

/// <summary>
/// Zeichnet Liniendiagramme (Zeitverlauf) direkt mit PdfSharp-Vektorgrafik in die Prüfberichte.
/// Lücken (fehlende Werte) unterbrechen die Linie, Verbindungsabbrüche werden als senkrechte
/// Markierung dargestellt (rot = Absturz, grau = Programm geschlossen).
/// </summary>
public static class PdfChartRenderer
{
    private static readonly XColor ColorDark = XColor.FromArgb(30, 41, 59);
    private static readonly XColor ColorGray = XColor.FromArgb(100, 116, 139);
    private static readonly XColor ColorGrid = XColor.FromArgb(226, 232, 240);
    private static readonly XColor ColorBorder = XColor.FromArgb(226, 232, 240);
    private static readonly XColor ColorCrash = XColor.FromArgb(220, 38, 38);
    private static readonly XColor ColorClosed = XColor.FromArgb(100, 116, 139);

    public const int MaxPoints = 360;

    public static void Draw(
        XGraphics gfx,
        XRect box,
        IReadOnlyList<TelemetrySnapshotPointDto> samplesChronological,
        IReadOnlyList<PdfChartSeries> seriesDefs,
        PdfChartOptions options)
    {
        var fontTitle = new XFont("Segoe UI", 10.5, XFontStyleEx.Bold);
        var fontLegend = new XFont("Segoe UI", 7.5, XFontStyleEx.Regular);
        var fontAxis = new XFont("Segoe UI", 7, XFontStyleEx.Regular);

        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 255, 255)), box);
        gfx.DrawRectangle(new XPen(ColorBorder, 1), box);
        gfx.DrawString(options.Title, fontTitle, new XSolidBrush(ColorDark), box.X + 10, box.Y + 15);

        var samples = Thin(samplesChronological, MaxPoints);

        // Nur Reihen mit mindestens einem Messwert
        var series = seriesDefs
            .Select(def => (Def: def, Values: samples.Select(s => Clean(def.Selector(s))).ToArray()))
            .Where(x => x.Values.Any(v => v.HasValue))
            .ToList();

        bool hasRight = series.Any(s => s.Def.RightAxis);

        // Legende (eine Zeile, bei Bedarf zweite)
        double lx = box.X + 10, ly = box.Y + 27;
        foreach (var (def, values) in series)
        {
            var vals = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            string text = $"{def.Label}: {vals.Min():F1}–{vals.Max():F1} {def.Unit}";
            double w = gfx.MeasureString(text, fontLegend).Width + 22;
            if (lx + w > box.Right - 8)
            {
                lx = box.X + 10;
                ly += 11;
            }
            gfx.DrawLine(new XPen(def.Color, 2.2), lx, ly - 2.5, lx + 12, ly - 2.5);
            gfx.DrawString(text, fontLegend, new XSolidBrush(ColorDark), lx + 15, ly);
            lx += w;
        }

        double plotLeft = box.X + 38;
        double plotRight = box.Right - (hasRight ? 38 : 12);
        double plotTop = ly + 10;
        double plotBottom = box.Bottom - 20;
        var plot = new XRect(plotLeft, plotTop, plotRight - plotLeft, plotBottom - plotTop);

        if (series.Count == 0 || samples.Count == 0 || plot.Height < 30)
        {
            gfx.DrawString(options.EmptyText, new XFont("Segoe UI", 9, XFontStyleEx.Italic), new XSolidBrush(ColorGray),
                new XRect(box.X, box.Y + 20, box.Width, box.Height - 20), XStringFormats.Center);
            return;
        }

        var (lMin, lMax) = Range(series.Where(s => !s.Def.RightAxis).SelectMany(s => s.Values), options.LeftMin, options.LeftMax, options.ThresholdValue);
        var (rMin, rMax) = Range(series.Where(s => s.Def.RightAxis).SelectMany(s => s.Values), options.RightMin, options.RightMax, null);

        // Rasterlinien + Achsenbeschriftung (5 Teilungen)
        var gridPen = new XPen(ColorGrid, 0.6);
        for (int i = 0; i <= 5; i++)
        {
            double y = plot.Bottom - plot.Height * i / 5.0;
            gfx.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            double lv = lMin + (lMax - lMin) * i / 5.0;
            gfx.DrawString(FormatAxis(lv), fontAxis, new XSolidBrush(ColorGray), new XRect(box.X + 2, y - 5, plotLeft - box.X - 6, 10), XStringFormats.CenterRight);
            if (hasRight)
            {
                double rv = rMin + (rMax - rMin) * i / 5.0;
                gfx.DrawString(FormatAxis(rv), fontAxis, new XSolidBrush(ColorGray), new XRect(plot.Right + 4, y - 5, box.Right - plot.Right - 6, 10), XStringFormats.CenterLeft);
            }
        }
        gfx.DrawString(options.LeftAxisUnit, fontAxis, new XSolidBrush(ColorGray), box.X + 6, plot.Top - 4);
        if (hasRight)
        {
            gfx.DrawString(options.RightAxisUnit, fontAxis, new XSolidBrush(ColorGray), new XRect(plot.Right, plot.Top - 12, box.Right - plot.Right - 6, 10), XStringFormats.CenterRight);
        }
        gfx.DrawRectangle(new XPen(ColorBorder, 0.8), plot);

        int n = samples.Count;
        double X(int i) => n <= 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * i / (n - 1);
        double Y(double v, bool right)
        {
            double min = right ? rMin : lMin, max = right ? rMax : lMax;
            double t = max - min <= 0 ? 0.5 : (v - min) / (max - min);
            return plot.Bottom - Math.Clamp(t, 0, 1) * plot.Height;
        }

        // Grenzwert (z.B. 85 °C)
        if (options.ThresholdValue.HasValue && options.ThresholdValue.Value >= lMin && options.ThresholdValue.Value <= lMax)
        {
            double ty = Y(options.ThresholdValue.Value, false);
            gfx.DrawLine(new XPen(ColorCrash, 0.9) { DashStyle = XDashStyle.Dash }, plot.Left, ty, plot.Right, ty);
            if (!string.IsNullOrEmpty(options.ThresholdLabel))
                gfx.DrawString(options.ThresholdLabel, fontAxis, new XSolidBrush(ColorCrash), new XRect(plot.Left, ty - 10, plot.Width - 4, 9), XStringFormats.CenterRight);
        }

        // Verbindungsabbrüche markieren
        for (int i = 0; i < n; i++)
        {
            if (samples[i].IsOnline) continue;
            bool closed = string.Equals(samples[i].StatusText, "Programm geschlossen", StringComparison.OrdinalIgnoreCase);
            var pen = new XPen(closed ? ColorClosed : ColorCrash, 0.9) { DashStyle = XDashStyle.Dot };
            gfx.DrawLine(pen, X(i), plot.Top, X(i), plot.Bottom);
        }

        // Linien (Lücken unterbrechen die Linie)
        foreach (var (def, values) in series)
        {
            var segments = new List<List<XPoint>>();
            List<XPoint>? current = null;
            for (int i = 0; i < n; i++)
            {
                if (!values[i].HasValue || !samples[i].IsOnline)
                {
                    current = null;
                    continue;
                }
                current ??= new List<XPoint>();
                if (current.Count == 0) segments.Add(current);
                current.Add(new XPoint(X(i), Y(values[i]!.Value, def.RightAxis)));
            }

            foreach (var seg in segments)
            {
                if (def.Fill && seg.Count > 1)
                {
                    var poly = new List<XPoint>(seg) { new(seg[^1].X, plot.Bottom), new(seg[0].X, plot.Bottom) };
                    gfx.DrawPolygon(new XSolidBrush(XColor.FromArgb(30, def.Color.R, def.Color.G, def.Color.B)), poly.ToArray(), XFillMode.Winding);
                }

                if (seg.Count == 1)
                    gfx.DrawEllipse(new XSolidBrush(def.Color), seg[0].X - 1.5, seg[0].Y - 1.5, 3, 3);
                else
                    gfx.DrawLines(new XPen(def.Color, 1.4) { LineJoin = XLineJoin.Round }, seg.ToArray());
            }
        }

        // Zeitachse: 5 Beschriftungen
        int labels = Math.Min(5, n);
        for (int k = 0; k < labels; k++)
        {
            int i = labels == 1 ? 0 : (int)Math.Round((n - 1) * k / (double)(labels - 1));
            string t = TimeLabel(samples[i]);
            var fmt = k == 0 ? XStringFormats.TopLeft : k == labels - 1 ? XStringFormats.TopRight : XStringFormats.TopCenter;
            double x = X(i);
            var rect = k == 0 ? new XRect(x, plot.Bottom + 4, 60, 10)
                : k == labels - 1 ? new XRect(x - 60, plot.Bottom + 4, 60, 10)
                : new XRect(x - 30, plot.Bottom + 4, 60, 10);
            gfx.DrawString(t, fontAxis, new XSolidBrush(ColorGray), rect, fmt);
        }
    }

    private static double? Clean(double? v)
        => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value) ? v : null;

    private static string TimeLabel(TelemetrySnapshotPointDto s)
        => !string.IsNullOrWhiteSpace(s.TimeShortDe)
            ? s.TimeShortDe!
            : (s.TimestampDe != null && s.TimestampDe.Length >= 19 ? s.TimestampDe.Substring(11, 8) : "");

    private static string FormatAxis(double v)
        => Math.Abs(v) >= 100 || Math.Abs(v - Math.Round(v)) < 0.05 ? $"{v:F0}" : $"{v:F1}";

    /// <summary>Gleichmäßig ausdünnen, erster und letzter Punkt sowie alle Verbindungsabbrüche bleiben erhalten.</summary>
    private static List<TelemetrySnapshotPointDto> Thin(IReadOnlyList<TelemetrySnapshotPointDto> samples, int max)
    {
        if (samples.Count <= max) return samples.ToList();
        var result = new List<TelemetrySnapshotPointDto>(max + 16);
        double step = (samples.Count - 1) / (double)(max - 1);
        int last = -1;
        for (int k = 0; k < max; k++)
        {
            int idx = (int)Math.Round(k * step);
            for (int j = last + 1; j < idx; j++)
            {
                if (!samples[j].IsOnline) result.Add(samples[j]);
            }
            if (idx != last) result.Add(samples[idx]);
            last = idx;
        }
        return result;
    }

    private static (double Min, double Max) Range(IEnumerable<double?> values, double? fixedMin, double? fixedMax, double? includeValue)
    {
        var vals = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (includeValue.HasValue) vals.Add(includeValue.Value);
        if (vals.Count == 0) return (fixedMin ?? 0, fixedMax ?? 100);

        double min = fixedMin ?? vals.Min();
        double max = fixedMax ?? vals.Max();
        if (max - min < 1e-6)
        {
            min -= 1;
            max += 1;
        }

        // "Schöne" Teilung: Schrittweite 1/2/5 × 10^n bei 5 Teilungen
        double rawStep = (max - min) / 5.0;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double norm = rawStep / mag;
        double step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
        double niceMin = fixedMin ?? Math.Floor(min / step) * step;
        double niceMax = fixedMax ?? niceMin + step * 5;
        while (niceMax < max - 1e-9 && fixedMax == null)
        {
            step *= 2;
            niceMin = fixedMin ?? Math.Floor(min / step) * step;
            niceMax = niceMin + step * 5;
        }
        return (niceMin, niceMax);
    }
}
