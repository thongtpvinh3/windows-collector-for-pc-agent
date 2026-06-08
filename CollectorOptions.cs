namespace WindowsDeviceJsonCollector;

public sealed class CollectorOptions
{
    public string AgentId { get; init; } = Environment.MachineName;

    public long Sequence { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public string? OutputFile { get; init; } = "snapshot.json";

    // Null means collect all devices.
    public HashSet<DeviceType>? Devices { get; init; }

    public static CollectorOptions Parse(string[] args)
    {
        return new CollectorOptions
        {
            AgentId = GetValue(args, "--agent-id") ?? Environment.MachineName,
            Sequence = GetLongValue(args, "--sequence") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            OutputFile = GetValue(args, "--output") ?? "snapshot.json",
            Devices = ParseDevices(GetValue(args, "--devices"))
        };
    }

    private static string? GetValue(string[] args, string key)
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

    private static long? GetLongValue(string[] args, string key)
    {
        string? value = GetValue(args, key);
        return long.TryParse(value, out long result) ? result : null;
    }

    private static HashSet<DeviceType>? ParseDevices(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = new HashSet<DeviceType>();

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            if (TryParseDeviceType(part, out DeviceType deviceType))
            {
                result.Add(deviceType);
            }
            else
            {
                Console.Error.WriteLine($"Unknown device type: {part}");
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static bool TryParseDeviceType(string value, out DeviceType deviceType)
    {
        string normalized = value.Trim().ToUpperInvariant();

        switch (normalized)
        {
            case "CPU":
                deviceType = DeviceType.CPU;
                return true;

            case "RAM":
            case "MEMORY":
                deviceType = DeviceType.RAM;
                return true;

            case "GPU":
                deviceType = DeviceType.GPU;
                return true;

            case "DISK":
            case "STORAGE":
            case "SSD":
            case "HDD":
                deviceType = DeviceType.DISK;
                return true;

            case "MAINBOARD":
            case "MOTHERBOARD":
            case "BOARD":
                deviceType = DeviceType.MAINBOARD;
                return true;

            case "NETWORK":
                deviceType = DeviceType.NETWORK;
                return true;

            case "BATTERY":
                deviceType = DeviceType.BATTERY;
                return true;

            case "FAN":
                deviceType = DeviceType.FAN;
                return true;

            case "UNKNOWN":
                deviceType = DeviceType.UNKNOWN;
                return true;

            default:
                deviceType = DeviceType.UNKNOWN;
                return false;
        }
    }
}
