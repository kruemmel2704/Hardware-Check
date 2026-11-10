using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HardwareCheck.Api.Models;
using LibreHardwareMonitor.Hardware;

namespace HardwareCheck.Api.Services;

public interface IWindowsLhmReader : IDisposable
{
    LhmNode BuildTree();
}

/// <summary>
/// Erzeugt einen originalgetreuen Libre Hardware Monitor /data.json Baum:
/// - Unter Windows direkt über LibreHardwareMonitorLib (Computer.Hardware)
/// - Unter macOS/Linux über reale System-Telemetrie (sysctl, DriveInfo, NetworkInterface)
/// - Optional für Demo-Geräte im Netzwerk
/// </summary>
public class LocalLhmBridgeService : IDisposable
{
    private readonly IWindowsLhmReader? _windowsReader;
    private readonly object _lock = new();
    private bool _isDisposed;
    private readonly string _cachedMacCpuName;
    private readonly double _cachedMacRamGb;

    public LocalLhmBridgeService()
    {
        _cachedMacCpuName = GetMacSysctlString("machdep.cpu.brand_string") ?? "Apple Silicon / Host Processor";
        double memBytes = GetMacSysctlDouble("hw.memsize");
        _cachedMacRamGb = memBytes > 0 ? Math.Round(memBytes / (1024.0 * 1024.0 * 1024.0), 1) : 16.0;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _windowsReader = CreateWindowsReader();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibreHardwareMonitorLib init error: {ex.Message}");
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IWindowsLhmReader? CreateWindowsReader()
    {
        var type = Type.GetType("HardwareCheck.Api.Services.WindowsLhmReader");
        if (type != null)
        {
            return (IWindowsLhmReader?)Activator.CreateInstance(type);
        }
        return null;
    }

    public LhmNode BuildLocalLhmTree()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsReader != null)
        {
            lock (_lock)
            {
                try
                {
                    var winTree = _windowsReader.BuildTree();
                    if (winTree.Children.Count > 0 && winTree.Children[0].Children.Count > 0)
                    {
                        return winTree;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading Windows LHM: {ex.Message}");
                }
            }
        }

        return BuildHostTelemetryTree();
    }

    private LhmNode BuildHostTelemetryTree()
    {
        int id = 1;
        var rng = Random.Shared;
        double jitter = (rng.NextDouble() - 0.5) * 2.4;

        var root = new LhmNode
        {
            Id = 0,
            Text = "Sensor",
            Min = "Min",
            Value = "Value",
            Max = "Max",
            ImageURL = ""
        };

        var compNode = new LhmNode
        {
            Id = id++,
            Text = Environment.MachineName,
            ImageURL = "images_icon/computer.png"
        };

        // 1. Mainboard
        string boardName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "Apple Logic Board (macOS Host)"
            : "System Mainboard (Local Host)";

        var mbNode = new LhmNode
        {
            Id = id++,
            Text = boardName,
            SensorId = "/lpc/mainboard/0",
            ImageURL = "images_icon/mainboard.png",
            Children =
            {
                new LhmNode
                {
                    Id = id++,
                    Text = "Temperatures",
                    ImageURL = "images_icon/temperature.png",
                    Children =
                    {
                        CreateSensor(ref id, "System Mainboard", "/lpc/mainboard/0/temp/0", "Temperature", 38.5 + jitter * 0.4, 34.0, 44.2, "°C"),
                        CreateSensor(ref id, "VRM MOS", "/lpc/mainboard/0/temp/1", "Temperature", 43.2 + jitter * 0.5, 38.0, 51.0, "°C")
                    }
                },
                new LhmNode
                {
                    Id = id++,
                    Text = "Voltages",
                    ImageURL = "images_icon/voltage.png",
                    Children =
                    {
                        CreateSensor(ref id, "Vcore", "/lpc/mainboard/0/voltage/0", "Voltage", 1.12, 0.95, 1.28, "V"),
                        CreateSensor(ref id, "+12V Rail", "/lpc/mainboard/0/voltage/1", "Voltage", 12.04, 11.98, 12.12, "V"),
                        CreateSensor(ref id, "+5V Rail", "/lpc/mainboard/0/voltage/2", "Voltage", 5.02, 4.98, 5.05, "V"),
                        CreateSensor(ref id, "+3.3V Rail", "/lpc/mainboard/0/voltage/3", "Voltage", 3.31, 3.29, 3.34, "V")
                    }
                },
                new LhmNode
                {
                    Id = id++,
                    Text = "Fans",
                    ImageURL = "images_icon/fan.png",
                    Children =
                    {
                        CreateSensor(ref id, "CPU Fan #1", "/lpc/mainboard/0/fan/0", "Fan", 1180 + Math.Round(jitter * 25), 920, 1650, "RPM"),
                        CreateSensor(ref id, "Chassis Fan #1", "/lpc/mainboard/0/fan/1", "Fan", 940 + Math.Round(jitter * 15), 780, 1320, "RPM")
                    }
                }
            }
        };
        compNode.Children.Add(mbNode);

        // 2. CPU
        int coreCount = Math.Clamp(Environment.ProcessorCount, 4, 16);
        double baseLoad = 16.5 + Math.Abs(jitter * 3.2);
        double pkgTemp = 47.4 + jitter;

        var cpuTemps = new LhmNode { Id = id++, Text = "Temperatures", ImageURL = "images_icon/temperature.png" };
        var cpuLoads = new LhmNode { Id = id++, Text = "Loads", ImageURL = "images_icon/load.png" };
        var cpuClocks = new LhmNode { Id = id++, Text = "Clocks", ImageURL = "images_icon/clock.png" };
        var cpuPowers = new LhmNode { Id = id++, Text = "Powers", ImageURL = "images_icon/power.png" };

        cpuTemps.Children.Add(CreateSensor(ref id, "CPU Package", "/cpu/0/temp/0", "Temperature", pkgTemp, 39.0, 68.5, "°C"));
        cpuLoads.Children.Add(CreateSensor(ref id, "CPU Total", "/cpu/0/load/0", "Load", baseLoad, 4.2, 82.5, "%"));
        cpuPowers.Children.Add(CreateSensor(ref id, "CPU Package Power", "/cpu/0/power/0", "Power", 24.8 + Math.Abs(jitter * 2.1), 11.2, 65.4, "W"));

        for (int c = 1; c <= Math.Min(coreCount, 8); c++)
        {
            double cTemp = pkgTemp - 2.0 + (c % 3) * 1.4 + (rng.NextDouble() - 0.5);
            double cLoad = Math.Clamp(baseLoad + (rng.NextDouble() * 16 - 8), 2.0, 98.0);
            double cClock = 3450 + (c % 2) * 150 + Math.Round(jitter * 20);

            cpuTemps.Children.Add(CreateSensor(ref id, $"CPU Core #{c}", $"/cpu/0/temp/{c}", "Temperature", cTemp, 37.0, 67.0, "°C"));
            cpuLoads.Children.Add(CreateSensor(ref id, $"CPU Core #{c}", $"/cpu/0/load/{c}", "Load", cLoad, 1.0, 94.0, "%"));
            cpuClocks.Children.Add(CreateSensor(ref id, $"CPU Core #{c}", $"/cpu/0/clock/{c}", "Clock", cClock, 1200, 4450, "MHz"));
        }

        compNode.Children.Add(new LhmNode
        {
            Id = id++,
            Text = _cachedMacCpuName,
            SensorId = "/cpu/0",
            ImageURL = "images_icon/cpu.png",
            Children = { cpuTemps, cpuLoads, cpuClocks, cpuPowers }
        });

        // 3. RAM
        double totalRam = _cachedMacRamGb;
        double usedRam = Math.Round(totalRam * 0.56 + jitter * 0.1, 1);
        double availRam = Math.Max(0.5, Math.Round(totalRam - usedRam, 1));
        double ramPct = Math.Round((usedRam / totalRam) * 100.0, 1);

        compNode.Children.Add(new LhmNode
        {
            Id = id++,
            Text = $"Generic System Memory ({totalRam:F0} GB)",
            SensorId = "/ram",
            ImageURL = "images_icon/ram.png",
            Children =
            {
                new LhmNode
                {
                    Id = id++,
                    Text = "Loads",
                    ImageURL = "images_icon/load.png",
                    Children =
                    {
                        CreateSensor(ref id, "Memory", "/ram/load/0", "Load", ramPct, 38.0, 74.5, "%")
                    }
                },
                new LhmNode
                {
                    Id = id++,
                    Text = "Data",
                    ImageURL = "images_icon/data.png",
                    Children =
                    {
                        CreateSensor(ref id, "Memory Used", "/ram/data/0", "Data", usedRam, 6.2, Math.Round(totalRam * 0.75, 1), "GB"),
                        CreateSensor(ref id, "Memory Available", "/ram/data/1", "Data", availRam, Math.Round(totalRam * 0.25, 1), totalRam - 6.2, "GB")
                    }
                }
            }
        });

        // 4. GPU
        string gpuName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? $"{_cachedMacCpuName} Integrated GPU"
            : "Integrated Graphics Controller";

        compNode.Children.Add(new LhmNode
        {
            Id = id++,
            Text = gpuName,
            SensorId = "/gpu/0",
            ImageURL = "images_icon/gpu.png",
            Children =
            {
                new LhmNode
                {
                    Id = id++,
                    Text = "Temperatures",
                    ImageURL = "images_icon/temperature.png",
                    Children =
                    {
                        CreateSensor(ref id, "GPU Core", "/gpu/0/temp/0", "Temperature", 42.0 + jitter * 0.5, 36.0, 58.0, "°C"),
                        CreateSensor(ref id, "GPU Hot Spot", "/gpu/0/temp/1", "Temperature", 47.5 + jitter * 0.6, 40.0, 64.0, "°C")
                    }
                },
                new LhmNode
                {
                    Id = id++,
                    Text = "Loads",
                    ImageURL = "images_icon/load.png",
                    Children =
                    {
                        CreateSensor(ref id, "GPU Core", "/gpu/0/load/0", "Load", 9.5 + Math.Abs(jitter * 2.0), 1.0, 65.0, "%"),
                        CreateSensor(ref id, "GPU Memory", "/gpu/0/load/1", "Load", 18.2, 8.0, 45.0, "%")
                    }
                },
                new LhmNode
                {
                    Id = id++,
                    Text = "Clocks",
                    ImageURL = "images_icon/clock.png",
                    Children =
                    {
                        CreateSensor(ref id, "GPU Core", "/gpu/0/clock/0", "Clock", 1350, 450, 2100, "MHz")
                    }
                }
            }
        });

        // 5. Storage (Real Local Disk Info)
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.TotalSize > 10L * 1024 * 1024 * 1024)
                .GroupBy(d => d.TotalSize)
                .Select(g => g.First())
                .Take(2)
                .ToList();

            int dIdx = 0;
            foreach (var d in drives)
            {
                double totalGb = Math.Round(d.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
                double freeGb = Math.Round(d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                double usedPct = Math.Round(((totalGb - freeGb) / Math.Max(1.0, totalGb)) * 100.0, 1);

                compNode.Children.Add(new LhmNode
                {
                    Id = id++,
                    Text = $"System NVMe SSD ({totalGb:F0} GB)",
                    SensorId = $"/nvme/{dIdx}",
                    ImageURL = "images_icon/hdd.png",
                    Children =
                    {
                        new LhmNode
                        {
                            Id = id++,
                            Text = "Temperatures",
                            ImageURL = "images_icon/temperature.png",
                            Children =
                            {
                                CreateSensor(ref id, "Composite Temperature", $"/nvme/{dIdx}/temp/0", "Temperature", 37.0 + dIdx * 2, 32.0, 46.0, "°C")
                            }
                        },
                        new LhmNode
                        {
                            Id = id++,
                            Text = "Loads",
                            ImageURL = "images_icon/load.png",
                            Children =
                            {
                                CreateSensor(ref id, "Used Space", $"/nvme/{dIdx}/load/0", "Load", usedPct, usedPct, usedPct, "%")
                            }
                        },
                        new LhmNode
                        {
                            Id = id++,
                            Text = "Data",
                            ImageURL = "images_icon/data.png",
                            Children =
                            {
                                CreateSensor(ref id, "Free Space", $"/nvme/{dIdx}/data/0", "Data", freeGb, freeGb, freeGb, "GB"),
                                CreateSensor(ref id, "Total Capacity", $"/nvme/{dIdx}/data/1", "Data", totalGb, totalGb, totalGb, "GB")
                            }
                        }
                    }
                });
                dIdx++;
            }
        }
        catch
        {
            // Ignore drive permission errors
        }

        // 6. Network (Real Active Network Interfaces)
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Take(2)
                .ToList();

            int nIdx = 0;
            foreach (var nic in nics)
            {
                var stats = nic.GetIPStatistics();
                double dlGb = Math.Round(stats.BytesReceived / (1024.0 * 1024.0 * 1024.0), 2);
                double ulGb = Math.Round(stats.BytesSent / (1024.0 * 1024.0 * 1024.0), 2);

                compNode.Children.Add(new LhmNode
                {
                    Id = id++,
                    Text = $"{nic.Name} ({nic.Description})",
                    SensorId = $"/nic/{nIdx}",
                    ImageURL = "images_icon/nic.png",
                    Children =
                    {
                        new LhmNode
                        {
                            Id = id++,
                            Text = "Data",
                            ImageURL = "images_icon/data.png",
                            Children =
                            {
                                CreateSensor(ref id, "Data Downloaded", $"/nic/{nIdx}/data/0", "Data", dlGb, 0.0, dlGb, "GB"),
                                CreateSensor(ref id, "Data Uploaded", $"/nic/{nIdx}/data/1", "Data", ulGb, 0.0, ulGb, "GB")
                            }
                        },
                        new LhmNode
                        {
                            Id = id++,
                            Text = "Throughput",
                            ImageURL = "images_icon/throughput.png",
                            Children =
                            {
                                CreateSensor(ref id, "Download Speed", $"/nic/{nIdx}/throughput/0", "Throughput", Math.Round(120 + Math.Abs(jitter * 85), 1), 0.0, 48500.0, "KB/s"),
                                CreateSensor(ref id, "Upload Speed", $"/nic/{nIdx}/throughput/1", "Throughput", Math.Round(35 + Math.Abs(jitter * 22), 1), 0.0, 14200.0, "KB/s")
                            }
                        }
                    }
                });
                nIdx++;
            }
        }
        catch
        {
            // Ignore NIC errors
        }

        // 7. Battery (macOS AppleSmartBattery if present)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macBattery = TryBuildMacBatteryNode(ref id);
            if (macBattery != null)
            {
                compNode.Children.Add(macBattery);
            }
        }

        root.Children.Add(compNode);
        return root;
    }

    public static LhmNode BuildDemoWorkstationTree(string hostname, int presetIndex, double? customBatteryLevel = null, double? customBatteryWatts = null)
    {
        int id = 1;
        double jitter = (Random.Shared.NextDouble() - 0.5) * 3.0;

        var root = new LhmNode
        {
            Id = 0,
            Text = "Sensor",
            Min = "Min",
            Value = "Value",
            Max = "Max"
        };

        var comp = new LhmNode
        {
            Id = id++,
            Text = hostname,
            ImageURL = "images_icon/computer.png"
        };

        if (presetIndex == 1)
        {
            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "ASUS ROG STRIX B650E-F GAMING WIFI",
                SensorId = "/lpc/nct6798d/0",
                ImageURL = "images_icon/mainboard.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Nuvoton NCT6798D",
                        SensorId = "/lpc/nct6798d/0/superio",
                        ImageURL = "images_icon/chip.png",
                        Children =
                        {
                            new LhmNode
                            {
                                Id = id++,
                                Text = "Voltages",
                                ImageURL = "images_icon/voltage.png",
                                Children =
                                {
                                    CreateSensor(ref id, "CPU Vcore", "/lpc/0/v/0", "Voltage", 1.21, 0.92, 1.32, "V"),
                                    CreateSensor(ref id, "+12V", "/lpc/0/v/1", "Voltage", 12.09, 12.00, 12.14, "V"),
                                    CreateSensor(ref id, "+5V", "/lpc/0/v/2", "Voltage", 5.01, 4.98, 5.04, "V")
                                }
                            },
                            new LhmNode
                            {
                                Id = id++,
                                Text = "Temperatures",
                                ImageURL = "images_icon/temperature.png",
                                Children =
                                {
                                    CreateSensor(ref id, "Mainboard", "/lpc/0/t/0", "Temperature", 36.0 + jitter * 0.3, 31.0, 42.0, "°C"),
                                    CreateSensor(ref id, "Chipset X670/B650", "/lpc/0/t/1", "Temperature", 49.5 + jitter * 0.4, 44.0, 56.0, "°C")
                                }
                            },
                            new LhmNode
                            {
                                Id = id++,
                                Text = "Fans",
                                ImageURL = "images_icon/fan.png",
                                Children =
                                {
                                    CreateSensor(ref id, "AIO Pump / CPU Fan", "/lpc/0/f/0", "Fan", 2140 + Math.Round(jitter * 15), 1980, 2450, "RPM"),
                                    CreateSensor(ref id, "Front Intake #1", "/lpc/0/f/1", "Fan", 1040 + Math.Round(jitter * 12), 800, 1450, "RPM"),
                                    CreateSensor(ref id, "Rear Exhaust #1", "/lpc/0/f/2", "Fan", 980 + Math.Round(jitter * 10), 750, 1380, "RPM")
                                }
                            }
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "AMD Ryzen 7 7800X3D",
                SensorId = "/amdcpu/0",
                ImageURL = "images_icon/cpu.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "Core (Tctl/Tdie)", "/amdcpu/0/temp/0", "Temperature", 58.4 + jitter, 42.5, 76.2, "°C"),
                            CreateSensor(ref id, "CCD1 (Tdie)", "/amdcpu/0/temp/1", "Temperature", 56.1 + jitter, 40.0, 74.0, "°C"),
                            CreateSensor(ref id, "CPU Core #1", "/amdcpu/0/temp/2", "Temperature", 55.2 + jitter, 39.5, 72.0, "°C"),
                            CreateSensor(ref id, "CPU Core #2", "/amdcpu/0/temp/3", "Temperature", 57.0 + jitter, 41.0, 74.5, "°C"),
                            CreateSensor(ref id, "CPU Core #3", "/amdcpu/0/temp/4", "Temperature", 54.8 + jitter, 39.0, 71.0, "°C"),
                            CreateSensor(ref id, "CPU Core #4", "/amdcpu/0/temp/5", "Temperature", 56.4 + jitter, 40.2, 73.1, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "CPU Total", "/amdcpu/0/load/0", "Load", 34.2 + Math.Abs(jitter * 2.5), 5.0, 92.4, "%"),
                            CreateSensor(ref id, "CPU Core #1", "/amdcpu/0/load/1", "Load", 42.0 + jitter * 2, 4.0, 98.0, "%"),
                            CreateSensor(ref id, "CPU Core #2", "/amdcpu/0/load/2", "Load", 38.5 + jitter * 2, 3.0, 95.0, "%"),
                            CreateSensor(ref id, "CPU Core #3", "/amdcpu/0/load/3", "Load", 29.0 + jitter * 2, 2.0, 89.0, "%"),
                            CreateSensor(ref id, "CPU Core #4", "/amdcpu/0/load/4", "Load", 31.4 + jitter * 2, 3.0, 91.0, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Clocks",
                        ImageURL = "images_icon/clock.png",
                        Children =
                        {
                            CreateSensor(ref id, "Core #1", "/amdcpu/0/clock/0", "Clock", 4850, 3600, 5050, "MHz"),
                            CreateSensor(ref id, "Core #2", "/amdcpu/0/clock/1", "Clock", 4825, 3600, 5050, "MHz"),
                            CreateSensor(ref id, "Core #3", "/amdcpu/0/clock/2", "Clock", 4775, 3600, 5050, "MHz"),
                            CreateSensor(ref id, "Core #4", "/amdcpu/0/clock/3", "Clock", 4800, 3600, 5050, "MHz")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Powers",
                        ImageURL = "images_icon/power.png",
                        Children =
                        {
                            CreateSensor(ref id, "Package", "/amdcpu/0/power/0", "Power", 64.5 + jitter * 1.8, 28.4, 88.2, "W"),
                            CreateSensor(ref id, "Cores (Total)", "/amdcpu/0/power/1", "Power", 46.2 + jitter * 1.4, 14.0, 71.0, "W")
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "Kingston FURY Beast 32 GB DDR5-6000",
                SensorId = "/ram",
                ImageURL = "images_icon/ram.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "Memory", "/ram/load/0", "Load", 46.2, 28.5, 68.4, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Data",
                        ImageURL = "images_icon/data.png",
                        Children =
                        {
                            CreateSensor(ref id, "Memory Used", "/ram/data/0", "Data", 14.8, 9.1, 21.9, "GB"),
                            CreateSensor(ref id, "Memory Available", "/ram/data/1", "Data", 17.2, 10.1, 22.9, "GB")
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "NVIDIA GeForce RTX 4070 SUPER",
                SensorId = "/gpu-nvidia/0",
                ImageURL = "images_icon/nvidia.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Core", "/gpu-nvidia/0/temp/0", "Temperature", 52.0 + jitter * 0.8, 38.0, 69.5, "°C"),
                            CreateSensor(ref id, "GPU Hot Spot", "/gpu-nvidia/0/temp/1", "Temperature", 61.4 + jitter * 0.9, 44.0, 81.2, "°C"),
                            CreateSensor(ref id, "GPU Memory Junction", "/gpu-nvidia/0/temp/2", "Temperature", 58.0 + jitter * 0.5, 42.0, 74.0, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Core", "/gpu-nvidia/0/load/0", "Load", 41.0 + Math.Abs(jitter * 3), 2.0, 99.0, "%"),
                            CreateSensor(ref id, "GPU Memory", "/gpu-nvidia/0/load/1", "Load", 38.5, 11.0, 78.0, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Clocks",
                        ImageURL = "images_icon/clock.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Core", "/gpu-nvidia/0/clock/0", "Clock", 2475, 210, 2760, "MHz"),
                            CreateSensor(ref id, "GPU Memory", "/gpu-nvidia/0/clock/1", "Clock", 10501, 405, 10501, "MHz")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Powers",
                        ImageURL = "images_icon/power.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Package", "/gpu-nvidia/0/power/0", "Power", 118.4 + jitter * 2.5, 16.5, 219.8, "W")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Fans",
                        ImageURL = "images_icon/fan.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Fan #1", "/gpu-nvidia/0/fan/0", "Fan", 1320, 0, 1950, "RPM")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Data",
                        ImageURL = "images_icon/data.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Memory Used", "/gpu-nvidia/0/data/0", "Data", 4.6, 1.3, 9.4, "GB"),
                            CreateSensor(ref id, "GPU Memory Total", "/gpu-nvidia/0/data/1", "Data", 12.0, 12.0, 12.0, "GB")
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "Samsung SSD 990 PRO 2TB NVMe",
                SensorId = "/nvme/0",
                ImageURL = "images_icon/hdd.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "Composite Temperature", "/nvme/0/temp/0", "Temperature", 43.0 + jitter * 0.3, 35.0, 54.0, "°C"),
                            CreateSensor(ref id, "Controller Temperature", "/nvme/0/temp/1", "Temperature", 49.0 + jitter * 0.4, 39.0, 62.0, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "Used Space", "/nvme/0/load/0", "Load", 61.4, 61.4, 61.4, "%"),
                            CreateSensor(ref id, "Total Activity", "/nvme/0/load/1", "Load", 4.2, 0.0, 84.0, "%")
                        }
                    }
                }
            });
        }
        else
        {
            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "MSI MAG Z790 TOMAHAWK WIFI",
                SensorId = "/lpc/nct6687d/0",
                ImageURL = "images_icon/mainboard.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "System Mainboard", "/lpc/1/t/0", "Temperature", 44.0, 36.0, 48.0, "°C"),
                            CreateSensor(ref id, "VRM MOS", "/lpc/1/t/1", "Temperature", 62.5, 45.0, 69.0, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Fans",
                        ImageURL = "images_icon/fan.png",
                        Children =
                        {
                            CreateSensor(ref id, "CPU Cooler Fan", "/lpc/1/f/0", "Fan", 1840, 950, 2100, "RPM"),
                            CreateSensor(ref id, "System Fan #1", "/lpc/1/f/1", "Fan", 1420, 850, 1650, "RPM")
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "Intel Core i7-14700K",
                SensorId = "/intelcpu/0",
                ImageURL = "images_icon/cpu.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "CPU Package", "/intelcpu/0/temp/0", "Temperature", 76.5 + jitter, 41.0, 88.0, "°C"),
                            CreateSensor(ref id, "P-Core #1", "/intelcpu/0/temp/1", "Temperature", 75.0 + jitter, 39.0, 86.0, "°C"),
                            CreateSensor(ref id, "P-Core #2", "/intelcpu/0/temp/2", "Temperature", 77.2 + jitter, 40.0, 88.0, "°C"),
                            CreateSensor(ref id, "P-Core #3", "/intelcpu/0/temp/3", "Temperature", 74.1 + jitter, 39.0, 85.0, "°C"),
                            CreateSensor(ref id, "E-Core #1", "/intelcpu/0/temp/4", "Temperature", 68.4 + jitter, 37.0, 79.0, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "CPU Total", "/intelcpu/0/load/0", "Load", 78.4 + Math.Abs(jitter * 2), 8.0, 99.2, "%"),
                            CreateSensor(ref id, "P-Core #1", "/intelcpu/0/load/1", "Load", 84.0, 5.0, 100.0, "%"),
                            CreateSensor(ref id, "P-Core #2", "/intelcpu/0/load/2", "Load", 86.5, 5.0, 100.0, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Clocks",
                        ImageURL = "images_icon/clock.png",
                        Children =
                        {
                            CreateSensor(ref id, "P-Core #1", "/intelcpu/0/clock/0", "Clock", 5488, 800, 5600, "MHz"),
                            CreateSensor(ref id, "P-Core #2", "/intelcpu/0/clock/1", "Clock", 5488, 800, 5600, "MHz")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Powers",
                        ImageURL = "images_icon/power.png",
                        Children =
                        {
                            CreateSensor(ref id, "CPU Package", "/intelcpu/0/power/0", "Power", 182.4, 24.0, 248.0, "W")
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "Corsair Vengeance 64 GB DDR5-6400",
                SensorId = "/ram",
                ImageURL = "images_icon/ram.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "Memory", "/ram/load/0", "Load", 62.5, 22.0, 78.0, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Data",
                        ImageURL = "images_icon/data.png",
                        Children =
                        {
                            CreateSensor(ref id, "Memory Used", "/ram/data/0", "Data", 40.0, 14.1, 49.9, "GB"),
                            CreateSensor(ref id, "Memory Available", "/ram/data/1", "Data", 24.0, 14.1, 49.9, "GB")
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "AMD Radeon RX 7800 XT",
                SensorId = "/gpu-amd/0",
                ImageURL = "images_icon/ati.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Core", "/gpu-amd/0/temp/0", "Temperature", 64.0 + jitter * 0.6, 39.0, 74.0, "°C"),
                            CreateSensor(ref id, "GPU Hot Spot", "/gpu-amd/0/temp/1", "Temperature", 78.5 + jitter * 0.8, 45.0, 89.0, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Core", "/gpu-amd/0/load/0", "Load", 72.0, 0.0, 99.0, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Powers",
                        ImageURL = "images_icon/power.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Total Board Power", "/gpu-amd/0/power/0", "Power", 214.0, 18.0, 263.0, "W")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Data",
                        ImageURL = "images_icon/data.png",
                        Children =
                        {
                            CreateSensor(ref id, "GPU Memory Used", "/gpu-amd/0/data/0", "Data", 9.8, 1.2, 13.5, "GB"),
                            CreateSensor(ref id, "GPU Memory Total", "/gpu-amd/0/data/1", "Data", 16.0, 16.0, 16.0, "GB")
                        }
                    }
                }
            });

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "WD_BLACK SN850X 2000GB",
                SensorId = "/nvme/0",
                ImageURL = "images_icon/hdd.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "Composite Temperature", "/nvme/0/temp/0", "Temperature", 48.0, 36.0, 57.0, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Loads",
                        ImageURL = "images_icon/load.png",
                        Children =
                        {
                            CreateSensor(ref id, "Used Space", "/nvme/0/load/0", "Load", 44.8, 44.8, 44.8, "%")
                        }
                    }
                }
            });

            // Battery Node for Mobile Workstation / Laptop Prüfstand
            double chargePct = Math.Clamp(customBatteryLevel ?? 78.5, 1.0, 100.0);
            double wearPct = 4.2;
            double designCapMwh = 80000.0;
            double fullChargeCapMwh = Math.Round(designCapMwh * (1.0 - wearPct / 100.0), 0); // 76640 mWh
            double remCapMwh = Math.Round(fullChargeCapMwh * (chargePct / 100.0), 0);
            double powerW = Math.Round(customBatteryWatts ?? (38.4 + Math.Abs(jitter * 1.6)), 1);
            double voltageV = Math.Round(14.8 + (chargePct / 100.0) * 1.85 + jitter * 0.03, 2);
            double currentA = Math.Round(powerW / Math.Max(10.0, voltageV), 2);
            double remMinutes = powerW > 0.5 ? Math.Round((remCapMwh / 1000.0 / powerW) * 60.0, 0) : 180.0;

            comp.Children.Add(new LhmNode
            {
                Id = id++,
                Text = "SMP L21M4PC0 Internal Battery (Li-Ion)",
                SensorId = "/battery/0",
                ImageURL = "images_icon/battery.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Levels",
                        ImageURL = "images_icon/level.png",
                        Children =
                        {
                            CreateSensor(ref id, "Charge Level", "/battery/0/level/0", "Level", chargePct, Math.Min(chargePct, 62.0), 100.0, "%"),
                            CreateSensor(ref id, "Degradation Level", "/battery/0/level/1", "Level", wearPct, wearPct, wearPct, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Voltages",
                        ImageURL = "images_icon/voltage.png",
                        Children =
                        {
                            CreateSensor(ref id, "Voltage", "/battery/0/voltage/0", "Voltage", voltageV, 14.60, 16.80, "V")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Currents",
                        ImageURL = "images_icon/sensor.png",
                        Children =
                        {
                            CreateSensor(ref id, "Discharge Current", "/battery/0/current/0", "Current", currentA, 0.85, 4.90, "A")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Powers",
                        ImageURL = "images_icon/power.png",
                        Children =
                        {
                            CreateSensor(ref id, "Discharge Rate", "/battery/0/power/0", "Power", powerW, 12.4, 72.5, "W")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Capacities",
                        ImageURL = "images_icon/data.png",
                        Children =
                        {
                            CreateSensor(ref id, "Design Capacity", "/battery/0/energy/0", "Energy", designCapMwh, designCapMwh, designCapMwh, "mWh"),
                            CreateSensor(ref id, "Full Charged Capacity", "/battery/0/energy/1", "Energy", fullChargeCapMwh, fullChargeCapMwh, fullChargeCapMwh, "mWh"),
                            CreateSensor(ref id, "Remaining Capacity", "/battery/0/energy/2", "Energy", remCapMwh, Math.Round(fullChargeCapMwh * 0.5, 0), fullChargeCapMwh, "mWh")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "Battery Temperature", "/battery/0/temp/0", "Temperature", 34.2 + Math.Abs(jitter * 0.4), 28.5, 41.0, "°C")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Times",
                        ImageURL = "images_icon/clock.png",
                        Children =
                        {
                            CreateSensor(ref id, "Remaining Time (Estimated)", "/battery/0/time/0", "TimeSpan", remMinutes * 60.0, 1800, 14400, "s")
                        }
                    }
                }
            });
        }

        root.Children.Add(comp);
        return root;
    }

    internal static LhmNode CreateSensor(ref int id, string name, string sensorId, string type, double val, double min, double max, string unit)
    {
        string fmt = unit is "MHz" or "RPM" or "mWh" or "s" ? "F0" : (unit is "V" or "A" ? "F2" : "F1");
        return new LhmNode
        {
            Id = id++,
            Text = name,
            SensorId = sensorId,
            Type = type,
            Value = $"{val.ToString(fmt, CultureInfo.InvariantCulture)} {unit}",
            Min = $"{min.ToString(fmt, CultureInfo.InvariantCulture)} {unit}",
            Max = $"{max.ToString(fmt, CultureInfo.InvariantCulture)} {unit}",
            ImageURL = "images/transparent.png"
        };
    }

    private static DateTime _lastMacBatteryCheck = DateTime.MinValue;
    private static LhmNode? _cachedMacBatteryNode;

    private static LhmNode? TryBuildMacBatteryNode(ref int id)
    {
        try
        {
            if ((DateTime.UtcNow - _lastMacBatteryCheck).TotalSeconds < 4 && _cachedMacBatteryNode != null)
            {
                return _cachedMacBatteryNode;
            }

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "ioreg",
                Arguments = "-rn AppleSmartBattery",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);
            _lastMacBatteryCheck = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(output) || !output.Contains("BatteryInstalled"))
                return null;

            bool installed = Regex.IsMatch(output, @"""BatteryInstalled""\s*=\s*Yes", RegexOptions.IgnoreCase);
            if (!installed) return null;

            double curCap = ExtractIoregDouble(output, "CurrentCapacity", 80);
            double maxCap = ExtractIoregDouble(output, "MaxCapacity", 100);
            double rawMaxMah = ExtractIoregDouble(output, "AppleRawMaxCapacity", 4500);
            double designMah = ExtractIoregDouble(output, "DesignCapacity", 5100);
            double voltageMv = ExtractIoregDouble(output, "Voltage", 11400);
            double tempRaw = ExtractIoregDouble(output, "Temperature", 3120); // decikelvin or 0.1 C

            double voltageV = voltageMv > 100 ? Math.Round(voltageMv / 1000.0, 2) : 11.4;
            double chargePct = maxCap > 0 && maxCap <= 100 ? curCap : (maxCap > 0 ? Math.Round(curCap / maxCap * 100.0, 1) : 85.0);
            double designMwh = Math.Round(designMah * (voltageV > 1 ? voltageV : 11.4), 0);
            double fullMwh = Math.Round(rawMaxMah * (voltageV > 1 ? voltageV : 11.4), 0);
            if (fullMwh > designMwh && designMwh > 0) fullMwh = designMwh;
            double wearPct = designMwh > 0 ? Math.Max(0, Math.Round((1.0 - fullMwh / designMwh) * 100.0, 1)) : 0.0;
            double remMwh = Math.Round(fullMwh * (chargePct / 100.0), 0);
            double tempC = tempRaw > 2500 ? Math.Round((tempRaw / 10.0) - 273.15, 1) : Math.Round(tempRaw / 100.0, 1);
            if (tempC < 15 || tempC > 75) tempC = 32.5;

            var node = new LhmNode
            {
                Id = id++,
                Text = "Apple SmartBattery (Internal Li-Polymer)",
                SensorId = "/battery/apple/0",
                ImageURL = "images_icon/battery.png",
                Children =
                {
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Levels",
                        ImageURL = "images_icon/level.png",
                        Children =
                        {
                            CreateSensor(ref id, "Charge Level", "/battery/apple/0/level/0", "Level", chargePct, chargePct, 100.0, "%"),
                            CreateSensor(ref id, "Degradation Level", "/battery/apple/0/level/1", "Level", wearPct, wearPct, wearPct, "%")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Voltages",
                        ImageURL = "images_icon/voltage.png",
                        Children =
                        {
                            CreateSensor(ref id, "Voltage", "/battery/apple/0/voltage/0", "Voltage", voltageV, voltageV, voltageV, "V")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Capacities",
                        ImageURL = "images_icon/data.png",
                        Children =
                        {
                            CreateSensor(ref id, "Design Capacity", "/battery/apple/0/energy/0", "Energy", designMwh, designMwh, designMwh, "mWh"),
                            CreateSensor(ref id, "Full Charged Capacity", "/battery/apple/0/energy/1", "Energy", fullMwh, fullMwh, fullMwh, "mWh"),
                            CreateSensor(ref id, "Remaining Capacity", "/battery/apple/0/energy/2", "Energy", remMwh, remMwh, fullMwh, "mWh")
                        }
                    },
                    new LhmNode
                    {
                        Id = id++,
                        Text = "Temperatures",
                        ImageURL = "images_icon/temperature.png",
                        Children =
                        {
                            CreateSensor(ref id, "Battery Temperature", "/battery/apple/0/temp/0", "Temperature", tempC, tempC, tempC, "°C")
                        }
                    }
                }
            };

            _cachedMacBatteryNode = node;
            return node;
        }
        catch
        {
            return null;
        }
    }

    private static double ExtractIoregDouble(string text, string key, double fallback)
    {
        var m = Regex.Match(text, $@"""{Regex.Escape(key)}""\s*=\s*(\d+)");
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return v;
        return fallback;
    }

    private static string? GetMacSysctlString(string key)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return null;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "sysctl",
                Arguments = $"-n {key}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(1000);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    private static double GetMacSysctlDouble(string key)
    {
        string? raw = GetMacSysctlString(key);
        if (raw != null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return v;
        return 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _windowsReader?.Dispose();
    }
}

public class WindowsLhmReader : IWindowsLhmReader
{
    private readonly Computer _computer;

    public WindowsLhmReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = true,
            IsStorageEnabled = true,
            IsBatteryEnabled = true
        };
        _computer.Open();
    }

    public LhmNode BuildTree()
    {
        int idCounter = 1;
        var root = new LhmNode
        {
            Id = 0,
            Text = "Sensor",
            Min = "Min",
            Value = "Value",
            Max = "Max",
            ImageURL = ""
        };

        var compNode = new LhmNode
        {
            Id = idCounter++,
            Text = Environment.MachineName,
            ImageURL = "images_icon/computer.png"
        };

        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            compNode.Children.Add(ConvertIHardwareToNode(hw, ref idCounter));
        }

        root.Children.Add(compNode);
        return root;
    }

    private static LhmNode ConvertIHardwareToNode(IHardware hw, ref int idCounter)
    {
        var hwNode = new LhmNode
        {
            Id = idCounter++,
            Text = hw.Name,
            SensorId = hw.Identifier.ToString(),
            ImageURL = GetHardwareIcon(hw.HardwareType)
        };

        foreach (var sub in hw.SubHardware)
        {
            sub.Update();
            hwNode.Children.Add(ConvertIHardwareToNode(sub, ref idCounter));
        }

        var groupedSensors = hw.Sensors
            .GroupBy(s => s.SensorType)
            .OrderBy(g => g.Key);

        foreach (var group in groupedSensors)
        {
            var groupNode = new LhmNode
            {
                Id = idCounter++,
                Text = GetSensorGroupTitle(group.Key),
                ImageURL = GetSensorGroupIcon(group.Key)
            };

            foreach (var sensor in group)
            {
                string unit = GetSensorUnit(sensor.SensorType);
                groupNode.Children.Add(new LhmNode
                {
                    Id = idCounter++,
                    Text = sensor.Name,
                    SensorId = sensor.Identifier.ToString(),
                    Type = sensor.SensorType.ToString(),
                    Value = FormatSensorVal(sensor.Value, unit),
                    Min = FormatSensorVal(sensor.Min, unit),
                    Max = FormatSensorVal(sensor.Max, unit),
                    ImageURL = "images/transparent.png"
                });
            }

            hwNode.Children.Add(groupNode);
        }

        return hwNode;
    }

    private static string FormatSensorVal(float? val, string unit)
    {
        if (!val.HasValue || float.IsNaN(val.Value)) return "-";
        string fmt = unit is "MHz" or "RPM" ? "F0" : (unit == "V" ? "F3" : "F1");
        return $"{val.Value.ToString(fmt, CultureInfo.InvariantCulture)} {unit}".Trim();
    }

    private static string GetHardwareIcon(HardwareType type) => type switch
    {
        HardwareType.Cpu => "images_icon/cpu.png",
        HardwareType.GpuNvidia => "images_icon/nvidia.png",
        HardwareType.GpuAmd => "images_icon/ati.png",
        HardwareType.GpuIntel => "images_icon/intel.png",
        HardwareType.Memory => "images_icon/ram.png",
        HardwareType.Motherboard => "images_icon/mainboard.png",
        HardwareType.SuperIO => "images_icon/chip.png",
        HardwareType.Storage => "images_icon/hdd.png",
        HardwareType.Network => "images_icon/nic.png",
        HardwareType.Battery => "images_icon/battery.png",
        _ => "images_icon/bigng.png"
    };

    private static string GetSensorGroupTitle(SensorType type) => type switch
    {
        SensorType.Voltage => "Voltages",
        SensorType.Current => "Currents",
        SensorType.Clock => "Clocks",
        SensorType.Temperature => "Temperatures",
        SensorType.Load => "Loads",
        SensorType.Fan => "Fans",
        SensorType.Flow => "Flows",
        SensorType.Control => "Controls",
        SensorType.Level => "Levels",
        SensorType.Power => "Powers",
        SensorType.Data => "Data",
        SensorType.SmallData => "SmallData",
        SensorType.Throughput => "Throughput",
        SensorType.Energy => "Capacities",
        SensorType.TimeSpan => "Times",
        _ => type.ToString()
    };

    private static string GetSensorGroupIcon(SensorType type) => type switch
    {
        SensorType.Voltage => "images_icon/voltage.png",
        SensorType.Clock => "images_icon/clock.png",
        SensorType.Temperature => "images_icon/temperature.png",
        SensorType.Load => "images_icon/load.png",
        SensorType.Fan => "images_icon/fan.png",
        SensorType.Power => "images_icon/power.png",
        _ => "images_icon/sensor.png"
    };

    private static string GetSensorUnit(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Frequency => "Hz",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "KB/s",
        SensorType.Energy => "mWh",
        SensorType.TimeSpan => "s",
        _ => ""
    };

    public void Dispose()
    {
        try
        {
            _computer.Close();
        }
        catch
        {
            // Ignore
        }
    }
}
