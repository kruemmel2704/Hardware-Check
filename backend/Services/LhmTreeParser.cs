using System.Globalization;
using System.Text.RegularExpressions;
using HardwareCheck.Api.Models;

namespace HardwareCheck.Api.Services;

public static class LhmTreeParser
{
    private static readonly Regex NumberRegex = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    public static HardwareDeviceReportDto ParseTree(
        LhmNode root,
        string id,
        string displayLabel,
        string host,
        int port,
        string sourceType,
        long latencyMs)
    {
        LhmNode computerNode = root;
        if ((root.Text.Equals("Sensor", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(root.Text))
            && root.Children.Count > 0)
        {
            computerNode = root.Children[0];
        }

        string computerName = !string.IsNullOrWhiteSpace(computerNode.Text) ? computerNode.Text : displayLabel;

        var report = new HardwareDeviceReportDto
        {
            Id = id,
            Name = computerName,
            DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? computerName : displayLabel,
            Host = host,
            Port = port,
            EndpointUrl = $"http://{host}:{port}/data.json",
            SourceType = sourceType,
            SourceBadge = sourceType switch
            {
                "lhm-api" => $"LHM API (:{port})",
                "local-bridge" => $"Lokal + :{port} API",
                "demo" => $"Simulation (:{port})",
                _ => $"Port {port}"
            },
            IsOnline = true,
            LatencyMs = latencyMs,
            LastUpdated = DateTime.UtcNow,
            RawTree = root
        };

        var components = new List<HardwareComponentDto>();
        foreach (var hwNode in computerNode.Children)
        {
            var comp = ParseHardwareNode(hwNode);
            components.Add(comp);
        }

        report.Components = components;
        report.TotalHardwareCount = CountHardware(components);
        report.TotalSensorCount = CountSensors(components);
        report.Summary = BuildQuickMetrics(components);

        return report;
    }

    private static int CountHardware(List<HardwareComponentDto> components)
    {
        int count = 0;
        foreach (var c in components)
        {
            count++;
            count += CountHardware(c.SubHardware);
        }
        return count;
    }

    private static int CountSensors(List<HardwareComponentDto> components)
    {
        int count = 0;
        foreach (var c in components)
        {
            count += c.Sensors.Count;
            count += CountSensors(c.SubHardware);
        }
        return count;
    }

    private static HardwareComponentDto ParseHardwareNode(LhmNode hwNode)
    {
        var (category, categoryDe) = ClassifyHardware(hwNode);
        var comp = new HardwareComponentDto
        {
            Id = !string.IsNullOrWhiteSpace(hwNode.SensorId) ? hwNode.SensorId : $"hw-{hwNode.Id}",
            Name = hwNode.Text,
            Category = category,
            CategoryDe = categoryDe,
            ImageUrl = hwNode.ImageURL ?? string.Empty
        };

        foreach (var child in hwNode.Children)
        {
            if (IsSensorGroupNode(child))
            {
                string groupType = NormalizeSensorType(child.Text, child.Type);
                FlattenSensorsFromGroup(child, groupType, comp.Sensors);
            }
            else if (!string.IsNullOrWhiteSpace(child.Value) && child.Children.Count == 0)
            {
                string sType = NormalizeSensorType(child.Type ?? child.Text, child.Type);
                comp.Sensors.Add(CreateSensorDto(child, sType));
            }
            else
            {
                // SubHardware (e.g. SuperIO chip under Motherboard)
                var sub = ParseHardwareNode(child);
                comp.SubHardware.Add(sub);
            }
        }

        var allSensors = comp.Sensors.Concat(comp.SubHardware.SelectMany(s => s.Sensors)).ToList();

        // Primary temperature (exclude Distance to TjMax, Warning Temperature, Critical Temperature)
        var tempSensors = allSensors
            .Where(s => s.SensorType == "Temperature" && s.ValueNumeric.HasValue && !IsThresholdOrDistanceTemp(s.Name))
            .ToList();

        var primaryTempSensor = tempSensors.FirstOrDefault(s =>
            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Core Average", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Composite", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Equals("System", StringComparison.OrdinalIgnoreCase))
            ?? tempSensors.FirstOrDefault();

        if (primaryTempSensor != null)
        {
            comp.PrimaryTemp = primaryTempSensor.ValueNumeric;
        }

        // Primary load
        var loadSensors = allSensors.Where(s => s.SensorType == "Load" && s.ValueNumeric.HasValue).ToList();
        var primaryLoadSensor = loadSensors.FirstOrDefault(s =>
            s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Used Space", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Belegter", StringComparison.OrdinalIgnoreCase))
            ?? loadSensors.FirstOrDefault();

        if (primaryLoadSensor != null)
        {
            comp.PrimaryLoad = primaryLoadSensor.ValueNumeric;
        }

        // Primary extra metric
        var powerSensor = allSensors.FirstOrDefault(s => s.SensorType == "Power" && s.ValueNumeric.HasValue && s.ValueNumeric.Value > 0);
        var clockSensor = allSensors.FirstOrDefault(s => s.SensorType == "Clock" && s.ValueNumeric.HasValue && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase));
        var fanSensor = allSensors.FirstOrDefault(s => s.SensorType == "Fan" && s.ValueNumeric.HasValue && s.ValueNumeric.Value > 0);
        var dataSensor = allSensors.FirstOrDefault(s => s.SensorType == "Data" && s.ValueNumeric.HasValue);
        var throughputSensor = allSensors.FirstOrDefault(s => s.SensorType == "Throughput" && s.ValueNumeric.HasValue);

        if (powerSensor != null)
        {
            comp.PrimaryMetricLabel = powerSensor.Name;
            comp.PrimaryMetricValue = powerSensor.ValueFormatted;
        }
        else if (clockSensor != null)
        {
            comp.PrimaryMetricLabel = clockSensor.Name;
            comp.PrimaryMetricValue = clockSensor.ValueFormatted;
        }
        else if (fanSensor != null)
        {
            comp.PrimaryMetricLabel = fanSensor.Name;
            comp.PrimaryMetricValue = fanSensor.ValueFormatted;
        }
        else if (dataSensor != null)
        {
            comp.PrimaryMetricLabel = dataSensor.Name;
            comp.PrimaryMetricValue = dataSensor.ValueFormatted;
        }
        else if (throughputSensor != null)
        {
            comp.PrimaryMetricLabel = throughputSensor.Name;
            comp.PrimaryMetricValue = throughputSensor.ValueFormatted;
        }

        if (allSensors.Any(s => s.Status == "critical"))
            comp.Status = "critical";
        else if (allSensors.Any(s => s.Status == "warning"))
            comp.Status = "warning";
        else
            comp.Status = "optimal";

        return comp;
    }

    private static bool IsThresholdOrDistanceTemp(string sensorName)
    {
        if (string.IsNullOrWhiteSpace(sensorName)) return false;
        return sensorName.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
               sensorName.Contains("TjMax", StringComparison.OrdinalIgnoreCase) ||
               sensorName.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
               sensorName.Contains("Critical", StringComparison.OrdinalIgnoreCase);
    }

    private static void FlattenSensorsFromGroup(LhmNode groupNode, string groupType, List<SensorItemDto> target)
    {
        foreach (var child in groupNode.Children)
        {
            if (child.Children.Count > 0 && string.IsNullOrWhiteSpace(child.Value))
            {
                FlattenSensorsFromGroup(child, groupType, target);
            }
            else
            {
                string effectiveType = !string.IsNullOrWhiteSpace(child.Type)
                    ? NormalizeSensorType(child.Type, child.Type)
                    : groupType;
                target.Add(CreateSensorDto(child, effectiveType));
            }
        }
    }

    private static SensorItemDto CreateSensorDto(LhmNode node, string sensorType)
    {
        var (valNum, unitFromVal) = ParseValueAndUnit(node.Value);
        var (minNum, _) = ParseValueAndUnit(node.Min);
        var (maxNum, _) = ParseValueAndUnit(node.Max);

        if (string.IsNullOrWhiteSpace(sensorType) || sensorType == "Other")
        {
            sensorType = InferSensorTypeFromUnit(unitFromVal, node.Text);
        }

        string categoryDe = sensorType switch
        {
            "Temperature" => "Temperatur",
            "Load" => "Auslastung",
            "Clock" => "Taktfrequenz",
            "Voltage" => "Spannung",
            "Power" => "Leistung",
            "Fan" => "Lüfter",
            "Control" => "Steuerung",
            "Data" => "Datenmenge",
            "SmallData" => "Speicher (MB)",
            "Throughput" => "Durchsatz",
            "Level" => "Füllstand / Zustand",
            "Factor" => "Faktor / Zähler",
            "Timing" => "Speicher-Timing",
            _ => "Messwert"
        };

        string status = "optimal";
        if (sensorType == "Temperature" && valNum.HasValue && !IsThresholdOrDistanceTemp(node.Text))
        {
            if (valNum.Value >= 85) status = "critical";
            else if (valNum.Value >= 74) status = "warning";
        }
        else if (sensorType == "Load" && valNum.HasValue)
        {
            if (valNum.Value >= 95) status = "critical";
            else if (valNum.Value >= 85) status = "warning";
        }

        return new SensorItemDto
        {
            Id = !string.IsNullOrWhiteSpace(node.SensorId) ? node.SensorId : $"sensor-{node.Id}",
            Name = node.Text,
            SensorType = sensorType,
            CategoryDe = categoryDe,
            ValueFormatted = string.IsNullOrWhiteSpace(node.Value) ? "–" : node.Value,
            MinFormatted = string.IsNullOrWhiteSpace(node.Min) ? "–" : node.Min,
            MaxFormatted = string.IsNullOrWhiteSpace(node.Max) ? "–" : node.Max,
            ValueNumeric = valNum,
            MinNumeric = minNum,
            MaxNumeric = maxNum,
            Unit = unitFromVal,
            Status = status
        };
    }

    private static QuickMetricsDto BuildQuickMetrics(List<HardwareComponentDto> components)
    {
        var q = new QuickMetricsDto();

        // 1. Mainboard
        var mb = components.FirstOrDefault(c => c.Category == "Mainboard");
        if (mb != null)
        {
            q.MotherboardName = mb.Name;
            var mbSensors = mb.Sensors.Concat(mb.SubHardware.SelectMany(s => s.Sensors)).ToList();
            var sysTemp = mbSensors.FirstOrDefault(s => s.SensorType == "Temperature" && s.ValueNumeric.HasValue && (s.Name.Equals("System", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Mainboard", StringComparison.OrdinalIgnoreCase)));
            var vrmTemp = mbSensors.FirstOrDefault(s => s.SensorType == "Temperature" && s.ValueNumeric.HasValue && s.Name.Contains("VRM", StringComparison.OrdinalIgnoreCase));
            var cpuFan = mbSensors.FirstOrDefault(s => s.SensorType == "Fan" && s.ValueNumeric.HasValue && s.ValueNumeric.Value > 0 && (s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Pump", StringComparison.OrdinalIgnoreCase)))
                         ?? mbSensors.FirstOrDefault(s => s.SensorType == "Fan" && s.ValueNumeric.HasValue && s.ValueNumeric.Value > 0);

            q.MainboardTemp = sysTemp?.ValueNumeric ?? mb.PrimaryTemp;
            q.VrmTemp = vrmTemp?.ValueNumeric;
            q.CpuFanRpm = cpuFan?.ValueNumeric;
        }

        // 2. CPU
        var cpu = components.FirstOrDefault(c => c.Category == "CPU");
        if (cpu != null)
        {
            var cpuSensors = cpu.Sensors.Concat(cpu.SubHardware.SelectMany(s => s.Sensors)).ToList();
            q.CpuName = cpu.Name;
            q.CpuTemp = cpu.PrimaryTemp;

            var coreMaxTemp = cpuSensors.FirstOrDefault(s => s.SensorType == "Temperature" && s.ValueNumeric.HasValue && s.Name.Contains("Core Max", StringComparison.OrdinalIgnoreCase));
            q.CpuMaxCoreTemp = coreMaxTemp?.ValueNumeric ?? q.CpuTemp;

            var totalLoad = cpuSensors.FirstOrDefault(s => s.SensorType == "Load" && s.ValueNumeric.HasValue && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
            q.CpuLoad = totalLoad?.ValueNumeric ?? cpu.PrimaryLoad;

            var clocks = cpuSensors.Where(s => s.SensorType == "Clock" && s.ValueNumeric.HasValue && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase)).ToList();
            if (clocks.Count > 0)
            {
                q.CpuClockMhz = Math.Round(clocks.Average(c => c.ValueNumeric!.Value), 0);
            }

            var pkgPower = cpuSensors.FirstOrDefault(s => s.SensorType == "Power" && s.ValueNumeric.HasValue && (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)))
                           ?? cpuSensors.FirstOrDefault(s => s.SensorType == "Power" && s.ValueNumeric.HasValue);
            q.CpuPowerWatts = pkgPower?.ValueNumeric;

            var vCore = cpuSensors.FirstOrDefault(s => s.SensorType == "Voltage" && s.ValueNumeric.HasValue && (s.Name.Equals("CPU Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Vcore", StringComparison.OrdinalIgnoreCase)))
                        ?? cpuSensors.FirstOrDefault(s => s.SensorType == "Voltage" && s.ValueNumeric.HasValue);
            q.CpuVoltage = vCore?.ValueNumeric;
        }

        // 3. GPU (prefer dedicated GPU with temperature sensor over integrated GPU)
        var gpus = components.Where(c => c.Category == "GPU").ToList();
        var gpu = gpus.FirstOrDefault(g => g.PrimaryTemp.HasValue && (g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || g.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || g.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) || g.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase) || g.Name.Contains("RX ", StringComparison.OrdinalIgnoreCase)))
                  ?? gpus.FirstOrDefault(g => g.PrimaryTemp.HasValue)
                  ?? gpus.FirstOrDefault();

        if (gpu != null)
        {
            var gpuSensors = gpu.Sensors.Concat(gpu.SubHardware.SelectMany(s => s.Sensors)).ToList();
            q.GpuName = gpu.Name;
            q.GpuTemp = gpu.PrimaryTemp;

            var hotSpot = gpuSensors.FirstOrDefault(s => s.SensorType == "Temperature" && s.ValueNumeric.HasValue && s.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase));
            q.GpuHotSpotTemp = hotSpot?.ValueNumeric;

            var coreLoad = gpuSensors.FirstOrDefault(s => s.SensorType == "Load" && s.ValueNumeric.HasValue && (s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase)));
            q.GpuLoad = coreLoad?.ValueNumeric ?? gpu.PrimaryLoad;

            var gpuPower = gpuSensors.FirstOrDefault(s => s.SensorType == "Power" && s.ValueNumeric.HasValue && (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Board", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)))
                           ?? gpuSensors.FirstOrDefault(s => s.SensorType == "Power" && s.ValueNumeric.HasValue);
            q.GpuPowerWatts = gpuPower?.ValueNumeric;

            var gpuClock = gpuSensors.FirstOrDefault(s => s.SensorType == "Clock" && s.ValueNumeric.HasValue && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
            q.GpuClockMhz = gpuClock?.ValueNumeric;

            var vramUsed = gpuSensors.FirstOrDefault(s => (s.SensorType == "SmallData" || s.SensorType == "Data") && s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase));
            var vramTotal = gpuSensors.FirstOrDefault(s => (s.SensorType == "SmallData" || s.SensorType == "Data") && s.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase));
            if (vramUsed?.ValueNumeric != null)
            {
                q.GpuVramUsedGb = vramUsed.Unit.Equals("MB", StringComparison.OrdinalIgnoreCase)
                    ? Math.Round(vramUsed.ValueNumeric.Value / 1024.0, 2)
                    : vramUsed.ValueNumeric.Value;
            }
            if (vramTotal?.ValueNumeric != null)
            {
                q.GpuVramTotalGb = vramTotal.Unit.Equals("MB", StringComparison.OrdinalIgnoreCase)
                    ? Math.Round(vramTotal.ValueNumeric.Value / 1024.0, 2)
                    : vramTotal.ValueNumeric.Value;
            }

            var gpuFan = gpuSensors.FirstOrDefault(s => s.SensorType == "Fan" && s.ValueNumeric.HasValue && s.ValueNumeric.Value > 0)
                         ?? gpuSensors.FirstOrDefault(s => s.SensorType == "Fan" && s.ValueNumeric.HasValue);
            q.GpuFanRpm = gpuFan?.ValueNumeric;
        }

        // 4. RAM (prefer Total Memory / Generic Memory over Virtual Memory or DIMM)
        var rams = components.Where(c => c.Category == "RAM").ToList();
        var ram = rams.FirstOrDefault(r => r.Name.Contains("Total Memory", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Generic", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("DDR", StringComparison.OrdinalIgnoreCase))
                  ?? rams.FirstOrDefault(r => !r.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !r.Name.Contains("DIMM", StringComparison.OrdinalIgnoreCase))
                  ?? rams.FirstOrDefault();

        if (ram != null)
        {
            var ramSensors = ram.Sensors.Concat(ram.SubHardware.SelectMany(s => s.Sensors)).ToList();
            var load = ramSensors.FirstOrDefault(s => s.SensorType == "Load" && s.ValueNumeric.HasValue && !s.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                       ?? ramSensors.FirstOrDefault(s => s.SensorType == "Load" && s.ValueNumeric.HasValue);
            var used = ramSensors.FirstOrDefault(s => s.SensorType == "Data" && s.ValueNumeric.HasValue && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                       ?? ramSensors.FirstOrDefault(s => s.SensorType == "Data" && s.ValueNumeric.HasValue && s.Name.Contains("Belegt", StringComparison.OrdinalIgnoreCase));
            var avail = ramSensors.FirstOrDefault(s => s.SensorType == "Data" && s.ValueNumeric.HasValue && (s.Name.Contains("Available", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Verfügbar", StringComparison.OrdinalIgnoreCase)) && !s.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase));

            q.RamLoadPercent = load?.ValueNumeric;
            q.RamUsedGb = used?.ValueNumeric;
            q.RamAvailableGb = avail?.ValueNumeric;
            if (q.RamUsedGb.HasValue && q.RamAvailableGb.HasValue)
            {
                q.RamTotalGb = Math.Round(q.RamUsedGb.Value + q.RamAvailableGb.Value, 1);
                if (!q.RamLoadPercent.HasValue && q.RamTotalGb.Value > 0)
                {
                    q.RamLoadPercent = Math.Round((q.RamUsedGb.Value / q.RamTotalGb.Value) * 100.0, 1);
                }
            }
        }

        // 5. Storage (Primary SSD/NVMe)
        var storages = components.Where(c => c.Category == "Storage").ToList();
        var primaryStorage = storages.FirstOrDefault();
        if (primaryStorage != null)
        {
            var stSensors = primaryStorage.Sensors.Concat(primaryStorage.SubHardware.SelectMany(s => s.Sensors)).ToList();
            q.StorageName = primaryStorage.Name;
            q.StorageTemp = primaryStorage.PrimaryTemp;
            q.StorageUsedPercent = primaryStorage.PrimaryLoad;
            var lifeSensor = stSensors.FirstOrDefault(s => s.SensorType == "Level" && s.Name.Equals("Life", StringComparison.OrdinalIgnoreCase));
            q.StorageLifePercent = lifeSensor?.ValueNumeric;
        }

        q.StorageCount = storages.Count;
        q.NetworkCount = components.Count(c => c.Category == "Network");

        var allSensors = components.SelectMany(c => c.Sensors.Concat(c.SubHardware.SelectMany(sub => sub.Sensors))).ToList();
        q.FanCount = allSensors.Count(s => s.SensorType == "Fan");
        q.WarningCount = allSensors.Count(s => s.Status == "warning");
        q.CriticalCount = allSensors.Count(s => s.Status == "critical");

        return q;
    }

    private static (string Category, string CategoryDe) ClassifyHardware(LhmNode node)
    {
        string img = (node.ImageURL ?? string.Empty).ToLowerInvariant();
        string text = (node.Text ?? string.Empty).ToLowerInvariant();
        string sid = (node.SensorId ?? string.Empty).ToLowerInvariant();

        // Check GPU before CPU so "Apple M1 Integrated GPU" is classified as GPU
        if (img.Contains("nvidia") || img.Contains("ati") || img.Contains("amd") || img.Contains("gpu") || sid.Contains("gpu") || text.Contains("geforce") || text.Contains("radeon") || text.Contains("rtx") || text.Contains("gtx") || text.Contains("arc ") || text.Contains("graphics") || text.Contains("gpu"))
            return ("GPU", "Grafikkarte (GPU)");

        if (img.Contains("cpu") || sid.Contains("cpu") || text.Contains("ryzen") || text.Contains("intel core") || text.Contains("xeon") || text.Contains("apple m") || text.Contains("threadripper"))
            return ("CPU", "Prozessor (CPU)");

        if (img.Contains("ram") || sid.Contains("/ram") || text.Contains("memory") || text.Contains("arbeitsspeicher") || text.Contains("ddr4") || text.Contains("ddr5") || text.Contains("dimm"))
            return ("RAM", "Arbeitsspeicher (RAM)");

        if (img.Contains("hdd") || sid.Contains("nvme") || sid.Contains("hdd") || sid.Contains("ssd") || text.Contains("nvme") || text.Contains("ssd") || text.Contains("wd ") || text.Contains("wd_") || text.Contains("samsung ssd") || text.Contains("crucial") || text.Contains("seagate") || text.Contains("kingston"))
            return ("Storage", "Speicherlaufwerk (SSD/HDD)");

        if (img.Contains("mainboard") || img.Contains("chip") || sid.Contains("mainboard") || sid.Contains("lpc") || text.Contains("rog ") || text.Contains("b650") || text.Contains("z790") || text.Contains("x670") || text.Contains("b550") || text.Contains("h510") || text.Contains("nuvoton") || text.Contains("ite ") || text.Contains("mainboard") || text.Contains("motherboard") || text.Contains("logic board"))
            return ("Mainboard", "Mainboard & Sensor-Chip");

        if (img.Contains("nic") || sid.Contains("nic") || text.Contains("ethernet") || text.Contains("wi-fi") || text.Contains("wlan") || text.Contains("network") || text.Contains("netzwerk") || text.Contains("realtek") || text.Contains("i225") || text.Contains("i226"))
            return ("Network", "Netzwerkadapter (NIC)");

        if (img.Contains("battery") || sid.Contains("battery") || text.Contains("battery") || text.Contains("akku"))
            return ("Battery", "Akku / USV");

        if (img.Contains("bigng") || sid.Contains("controller") || text.Contains("fan") || text.Contains("aqua") || text.Contains("corsair"))
            return ("Controller", "Lüfter- & Kühlungs-Controller");

        return ("Other", "Systemkomponente");
    }

    private static bool IsSensorGroupNode(LhmNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Value)) return false;
        string t = (node.Text ?? string.Empty).Trim().ToLowerInvariant();
        return t is "voltage" or "voltages" or "spannung" or "spannungen"
            or "temperature" or "temperatures" or "temperatur" or "temperaturen"
            or "load" or "loads" or "auslastung" or "auslastungen" or "last"
            or "clock" or "clocks" or "takt" or "taktraten" or "taktfrequenzen"
            or "power" or "powers" or "leistung" or "leistungen"
            or "fan" or "fans" or "lüfter"
            or "flow" or "flows" or "durchfluss"
            or "control" or "controls" or "steuerungen" or "steuerung"
            or "level" or "levels" or "füllstand" or "füllstände" or "pegel"
            or "data" or "daten"
            or "smalldata"
            or "throughput" or "durchsatz"
            or "factor" or "factors" or "faktor" or "faktoren"
            or "timing" or "timings"
            or "current" or "stromstärke"
            or "frequency" or "frequencies" or "frequenzen";
    }

    private static string NormalizeSensorType(string? rawText, string? rawType)
    {
        string check = $"{rawType} {rawText}".ToLowerInvariant();
        if (check.Contains("temp")) return "Temperature";
        if (check.Contains("load") || check.Contains("auslastung") || check.Contains("last")) return "Load";
        if (check.Contains("clock") || check.Contains("takt")) return "Clock";
        if (check.Contains("volt") || check.Contains("spannung")) return "Voltage";
        if (check.Contains("power") || check.Contains("leistung")) return "Power";
        if (check.Contains("fan") || check.Contains("lüfter")) return "Fan";
        if (check.Contains("control") || check.Contains("steuerung")) return "Control";
        if (check.Contains("smalldata")) return "SmallData";
        if (check.Contains("data") || check.Contains("daten")) return "Data";
        if (check.Contains("throughput") || check.Contains("durchsatz")) return "Throughput";
        if (check.Contains("level") || check.Contains("füllstand")) return "Level";
        if (check.Contains("factor") || check.Contains("faktor")) return "Factor";
        if (check.Contains("timing")) return "Timing";
        return "Other";
    }

    private static string InferSensorTypeFromUnit(string unit, string sensorName)
    {
        string u = unit.ToLowerInvariant();
        if (u.Contains("°c") || u.Contains("°f")) return "Temperature";
        if (u == "%") return sensorName.Contains("Fan", StringComparison.OrdinalIgnoreCase) ? "Control" : "Load";
        if (u == "mhz" || u == "ghz") return "Clock";
        if (u == "v" || u == "mv") return "Voltage";
        if (u == "w") return "Power";
        if (u == "rpm") return "Fan";
        if (u == "gb") return "Data";
        if (u == "mb") return "SmallData";
        if (u == "ns") return "Timing";
        if (u.Contains("/s")) return "Throughput";
        return "Other";
    }

    private static (double? Value, string Unit) ParseValueAndUnit(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "-" || raw.Trim() == "–")
            return (null, string.Empty);

        var match = NumberRegex.Match(raw);
        if (!match.Success)
            return (null, string.Empty);

        string numStr = match.Value.Replace(',', '.');
        double? val = null;
        if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            val = Math.Round(parsed, 2);
        }

        string unit = raw[(match.Index + match.Length)..].Trim();
        return (val, unit);
    }
}
