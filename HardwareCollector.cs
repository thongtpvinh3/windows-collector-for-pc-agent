using System.Security.Cryptography;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace WindowsDeviceJsonCollector;

public sealed class HardwareCollector : IDisposable
{
    private readonly string _appVersion;
    private readonly Computer _computer;
    private bool _opened;

    public HardwareCollector(string appVersion)
    {
        _appVersion = appVersion;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true,
            IsNetworkEnabled = true,
            IsControllerEnabled = true,
            IsPsuEnabled = true,
            IsBatteryEnabled = true
        };
    }

    public AgentSnapshot Collect(CollectorOptions options)
    {
        OpenIfNeeded();

        string now = NowUtcString();

        var snapshot = new AgentSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString(),
            AgentId = options.AgentId,
            Hostname = Environment.MachineName,
            Os = Environment.OSVersion.ToString(),
            AppVersion = _appVersion,
            Sequence = options.Sequence,
            Timestamp = now,
            Devices = new List<DeviceMetricResult>()
        };

        foreach (var hardware in _computer.Hardware)
        {
            DeviceType currentType = MapDeviceType(hardware.HardwareType);

            if (!ShouldCollect(hardware, currentType, options.Devices))
            {
                continue;
            }

            snapshot.Devices.Add(ReadHardware(hardware, now));
        }

        return snapshot;
    }

    private void OpenIfNeeded()
    {
        if (_opened)
        {
            return;
        }

        _computer.Open();
        _opened = true;
    }

    public void Dispose()
    {
        if (_opened)
        {
            _computer.Close();
            _opened = false;
        }
    }

    private static bool ShouldCollect(IHardware hardware, DeviceType currentType, HashSet<DeviceType>? requestedTypes)
    {
        if (requestedTypes == null || requestedTypes.Count == 0)
        {
            return true;
        }

        if (requestedTypes.Contains(currentType))
        {
            return true;
        }

        // CPU temperature sensors are sometimes exposed through SuperIO.
        // Keep SuperIO when the user asks for CPU to avoid losing CPU temperature.
        if (requestedTypes.Contains(DeviceType.CPU) && hardware.HardwareType == HardwareType.SuperIO)
        {
            return true;
        }

        return false;
    }

    private static DeviceMetricResult ReadHardware(IHardware hardware, string collectedAt)
    {
        try
        {
            var result = new DeviceMetricResult
            {
                Device = CreateDeviceDescriptor(hardware),
                CollectedAt = collectedAt,
                Metrics = new List<MetricValue>()
            };

            CollectSensorsRecursive(hardware, result);

            if (result.Metrics.Count == 0)
            {
                result.Metrics.Add(new MetricValue
                {
                    Name = $"{MapDeviceType(hardware.HardwareType).ToString().ToLowerInvariant()}.metrics",
                    Value = null,
                    Unit = "",
                    Status = MetricStatus.NOT_AVAILABLE,
                    Message = "No sensors available for this hardware."
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            return new DeviceMetricResult
            {
                Device = CreateDeviceDescriptor(hardware),
                CollectedAt = collectedAt,
                Metrics = new List<MetricValue>
                {
                    new MetricValue
                    {
                        Name = "hardware.read.error",
                        Value = null,
                        Unit = "",
                        Status = MetricStatus.ERROR,
                        Message = ex.Message
                    }
                }
            };
        }
    }

    private static void CollectSensorsRecursive(IHardware hardware, DeviceMetricResult result)
    {
        hardware.Update();

        foreach (var sensor in hardware.Sensors)
        {
            result.Metrics.Add(CreateMetricValue(hardware, sensor));
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            CollectSensorsRecursive(subHardware, result);
        }
    }

    private static DeviceDescriptor CreateDeviceDescriptor(IHardware hardware)
    {
        DeviceType deviceType = MapDeviceType(hardware.HardwareType);

        return new DeviceDescriptor
        {
            DeviceId = CreateStableDeviceId(deviceType, hardware.Name, hardware.Identifier.ToString()),
            Type = deviceType,
            Name = hardware.Name,
            Vendor = ExtractVendor(hardware.Name),
            Model = hardware.Name,
            Attributes = new Dictionary<string, string>
            {
                ["hardwareType"] = hardware.HardwareType.ToString(),
                ["identifier"] = hardware.Identifier.ToString()
            }
        };
    }

    private static MetricValue CreateMetricValue(IHardware hardware, ISensor sensor)
    {
        string deviceType = MapDeviceType(hardware.HardwareType).ToString().ToLowerInvariant();

        if (hardware.HardwareType == HardwareType.SuperIO &&
            sensor.SensorType == SensorType.Temperature &&
            IsCpuTemperatureSensorName(sensor.Name))
        {
            deviceType = "cpu";
        }

        string sensorType = sensor.SensorType.ToString().ToLowerInvariant();
        string sensorName = NormalizeName(sensor.Name);
        float? rawValue = sensor.Value;

        if (!IsValidJsonNumber(rawValue))
        {
            return new MetricValue
            {
                Name = $"{deviceType}.{sensorType}.{sensorName}",
                Value = null,
                Unit = GetUnit(sensor.SensorType),
                Status = MetricStatus.NOT_AVAILABLE,
                Message = "Sensor value is null, NaN, Infinity, or -Infinity."
            };
        }

        if (deviceType == "cpu" && sensor.SensorType == SensorType.Temperature)
        {
            Console.Error.WriteLine(
                $"CPU TEMP JSON DEBUG | metric={deviceType}.{sensorType}.{sensorName} | value={sensor.Value}"
            );
        }

        return new MetricValue
        {
            Name = $"{deviceType}.{sensorType}.{sensorName}",
            Value = rawValue.Value,
            Unit = GetUnit(sensor.SensorType),
            Status = MetricStatus.OK,
            Message = ""
        };
    }

    public static AgentSnapshot CreateErrorSnapshot(string appVersion, string agentId, long sequence, Exception ex)
    {
        string now = NowUtcString();

        return new AgentSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Hostname = Environment.MachineName,
            Os = Environment.OSVersion.ToString(),
            AppVersion = appVersion,
            Sequence = sequence,
            Timestamp = now,
            Devices = new List<DeviceMetricResult>
            {
                new DeviceMetricResult
                {
                    Device = new DeviceDescriptor
                    {
                        DeviceId = "windows-collector",
                        Type = DeviceType.UNKNOWN,
                        Name = "Windows Hardware Collector",
                        Vendor = "",
                        Model = "",
                        Attributes = new Dictionary<string, string>()
                    },
                    CollectedAt = now,
                    Metrics = new List<MetricValue>
                    {
                        new MetricValue
                        {
                            Name = "collector.error",
                            Value = null,
                            Unit = "",
                            Status = MetricStatus.ERROR,
                            Message = ex.Message
                        }
                    }
                }
            }
        };
    }

    private static DeviceType MapDeviceType(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.Cpu => DeviceType.CPU,
            HardwareType.Memory => DeviceType.RAM,
            HardwareType.GpuNvidia => DeviceType.GPU,
            HardwareType.GpuAmd => DeviceType.GPU,
            HardwareType.GpuIntel => DeviceType.GPU,
            HardwareType.Storage => DeviceType.DISK,
            HardwareType.Motherboard => DeviceType.MAINBOARD,
            HardwareType.SuperIO => DeviceType.MAINBOARD,
            HardwareType.Network => DeviceType.NETWORK,
            HardwareType.Battery => DeviceType.BATTERY,
            _ => DeviceType.UNKNOWN
        };
    }

    private static string GetUnit(SensorType sensorType)
    {
        return sensorType switch
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
            SensorType.Factor => "factor",
            SensorType.Data => "GB",
            SensorType.SmallData => "MB",
            SensorType.Throughput => "B/s",
            SensorType.TimeSpan => "s",
            SensorType.Energy => "mWh",
            SensorType.Noise => "dBA",
            _ => ""
        };
    }

    private static bool IsCpuTemperatureSensorName(string name)
    {
        return name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Core", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Package", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidJsonNumber(float? value)
    {
        return value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value);
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder();
        bool lastWasUnderscore = false;

        foreach (char c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore)
            {
                builder.Append('_');
                lastWasUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }

    private static string CreateStableDeviceId(DeviceType type, string name, string identifier)
    {
        string raw = $"{type}|{name}|{identifier}".ToLowerInvariant();
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static string ExtractVendor(string hardwareName)
    {
        string name = hardwareName.ToLowerInvariant();

        if (name.Contains("intel")) return "Intel";
        if (name.Contains("amd")) return "AMD";
        if (name.Contains("nvidia")) return "NVIDIA";
        if (name.Contains("samsung")) return "Samsung";
        if (name.Contains("kingston")) return "Kingston";
        if (name.Contains("western digital")) return "Western Digital";
        if (name.Contains("wd ")) return "Western Digital";
        if (name.Contains("seagate")) return "Seagate";
        if (name.Contains("toshiba")) return "Toshiba";
        if (name.Contains("realtek")) return "Realtek";
        if (name.Contains("mediatek")) return "MediaTek";
        if (name.Contains("qualcomm")) return "Qualcomm";

        return "";
    }

    private static string NowUtcString()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
    }
}
