using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibreHardwareMonitor.Hardware;
using System.Text.Encodings.Web;

namespace WindowsDeviceJsonCollector;

public class Program
{
    private const string AppVersion = "1.0.0";

    public static int Main(string[] args)
    {
        string agentId = GetArgValue(args, "--agent-id") ?? Environment.MachineName;
        long sequence =
            GetLongArgValue(args, "--sequence") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? outputFile = GetArgValue(args, "--output") ?? "snapshot.json";

        var computer = new Computer
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

        try
        {
            computer.Open();

            string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

            var snapshot = new AgentSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString(),
                AgentId = agentId,
                Hostname = Environment.MachineName,
                Os = Environment.OSVersion.ToString(),
                AppVersion = AppVersion,
                Sequence = sequence,
                Timestamp = now,
                Devices = new List<DeviceMetricResult>(),
            };

            foreach (var hardware in computer.Hardware)
            {
                var deviceMetricResult = ReadHardware(hardware, now);

                if (deviceMetricResult != null)
                {
                    snapshot.Devices.Add(deviceMetricResult);
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            Console.OutputEncoding = Encoding.UTF8;

            string json = JsonSerializer.Serialize(snapshot, options);

            if (!string.IsNullOrWhiteSpace(outputFile))
            {
                WriteJsonToFile(outputFile, json);
            }
            else
            {
                Console.WriteLine(json);
            }

            return 0;
        }
        catch (Exception ex)
        {
            var errorSnapshot = CreateErrorSnapshot(agentId, sequence, ex);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            Console.OutputEncoding = Encoding.UTF8;

            string json = JsonSerializer.Serialize(JsonSerializer.Serialize(errorSnapshot, options), options);

            if (!string.IsNullOrWhiteSpace(outputFile))
            {
                WriteJsonToFile(outputFile, json);
            }
            else
            {
                Console.WriteLine(json);
            }

            return 1;
        }
        finally
        {
            computer.Close();
        }
    }

    private static void WriteJsonToFile(string outputFile, string json)
    {
        string? directory = Path.GetDirectoryName(outputFile);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputFile, json, Encoding.UTF8);
    }

    // public static void Main(string[] args)
    // {
    //     Console.WriteLine();

    //     var computer = new Computer
    //     {
    //         IsCpuEnabled = true,
    //         IsGpuEnabled = true,
    //         IsMemoryEnabled = true,
    //         IsStorageEnabled = true,
    //         IsMotherboardEnabled = true,
    //         IsNetworkEnabled = true,
    //         IsControllerEnabled = true,
    //         IsPsuEnabled = true,
    //         IsBatteryEnabled = true
    //     };

    //     computer.Open();

    //     foreach (var hardware in computer.Hardware)
    //     {
    //         PrintHardwareRecursive(hardware, 0);
    //     }

    //     computer.Close();
    // }

    private static void CollectSensorsRecursive(IHardware hardware, DeviceMetricResult result)
    {
        hardware.Update();

        foreach (var sensor in hardware.Sensors)
        {
            var metric = CreateMetricValue(hardware, sensor);
            result.Metrics.Add(metric);
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            CollectSensorsRecursive(subHardware, result);
        }
    }

    private static void PrintHardwareRecursive(IHardware hardware, int level)
    {
        string indent = new string(' ', level * 2);

        hardware.Update();

        Console.WriteLine($"{indent}==================================================");
        Console.WriteLine($"{indent}Hardware Name : {hardware.Name}");
        Console.WriteLine($"{indent}Hardware Type : {hardware.HardwareType}");
        Console.WriteLine($"{indent}Identifier    : {hardware.Identifier}");
        Console.WriteLine($"{indent}Sensor Count  : {hardware.Sensors.Length}");
        Console.WriteLine($"{indent}Sub Count     : {hardware.SubHardware.Length}");

        foreach (var sensor in hardware.Sensors)
        {
            Console.WriteLine(
                $"{indent}  Sensor | Type={sensor.SensorType} | Name={sensor.Name} | Value={sensor.Value} | Min={sensor.Min} | Max={sensor.Max}"
            );
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            PrintHardwareRecursive(subHardware, level + 1);
        }
    }

    private static void PrintHardware(IHardware hardware)
    {
        hardware.Update();

        Console.WriteLine($"Hardware: {hardware.Name} - {hardware.HardwareType}");

        foreach (var sensor in hardware.Sensors)
        {
            Console.WriteLine($"  Sensor: {sensor.Name}");
            Console.WriteLine($"    Type : {sensor.SensorType}");
            Console.WriteLine($"    Value: {sensor.Value}");
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            PrintHardware(subHardware);
        }
    }

    private static DeviceMetricResult? ReadHardware(IHardware hardware, string collectedAt)
    {
        try
        {
            var descriptor = CreateDeviceDescriptor(hardware);

            var result = new DeviceMetricResult
            {
                Device = descriptor,
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

    private static DeviceDescriptor CreateDeviceDescriptor(IHardware hardware)
    {
        var deviceType = MapDeviceType(hardware.HardwareType);

        string vendor = ExtractVendor(hardware.Name);
        string model = hardware.Name;

        var attributes = new Dictionary<string, string>
        {
            ["hardwareType"] = hardware.HardwareType.ToString(),
            ["identifier"] = hardware.Identifier.ToString()
        };

        string deviceId = CreateStableDeviceId(deviceType, hardware.Name, hardware.Identifier.ToString());

        return new DeviceDescriptor
        {
            DeviceId = deviceId,
            Type = deviceType,
            Name = hardware.Name,
            Vendor = vendor,
            Model = model,
            Attributes = attributes
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
        if (!value.HasValue)
        {
            return false;
        }

        return !float.IsNaN(value.Value) && !float.IsInfinity(value.Value);
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
            else
            {
                if (!lastWasUnderscore)
                {
                    builder.Append('_');
                    lastWasUnderscore = true;
                }
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

    private static string? GetArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static long? GetLongArgValue(string[] args, string key)
    {
        string? value = GetArgValue(args, key);

        if (long.TryParse(value, out long result))
        {
            return result;
        }

        return null;
    }

    private static AgentSnapshot CreateErrorSnapshot(string agentId, long sequence, Exception ex)
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        return new AgentSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Hostname = Environment.MachineName,
            Os = Environment.OSVersion.ToString(),
            AppVersion = AppVersion,
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
}

public class AgentSnapshot
{
    public string SnapshotId { get; set; } = "";

    public string AgentId { get; set; } = "";

    public string Hostname { get; set; } = "";

    public string Os { get; set; } = "";

    public string AppVersion { get; set; } = "";

    public long Sequence { get; set; }

    public string Timestamp { get; set; } = "";

    public List<DeviceMetricResult> Devices { get; set; } = new();
}

public class DeviceMetricResult
{
    public DeviceDescriptor Device { get; set; } = new();

    public string CollectedAt { get; set; } = "";

    public List<MetricValue> Metrics { get; set; } = new();
}

public class DeviceDescriptor
{
    public string DeviceId { get; set; } = "";

    public DeviceType Type { get; set; } = DeviceType.UNKNOWN;

    public string Name { get; set; } = "";

    public string Vendor { get; set; } = "";

    public string Model { get; set; } = "";

    public Dictionary<string, string> Attributes { get; set; } = new();
}

public enum DeviceType
{
    CPU,
    RAM,
    GPU,
    DISK,
    MAINBOARD,
    NETWORK,
    BATTERY,
    FAN,
    UNKNOWN,
}

public class MetricValue
{
    public string Name { get; set; } = "";

    public object? Value { get; set; }

    public string Unit { get; set; } = "";

    public MetricStatus Status { get; set; } = MetricStatus.NOT_AVAILABLE;

    public string Message { get; set; } = "";
}

public enum MetricStatus
{
    OK,
    NOT_AVAILABLE,
    TIMEOUT,
    ERROR,
}
