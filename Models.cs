namespace WindowsDeviceJsonCollector;

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

public class MetricValue
{
    public string Name { get; set; } = "";
    public object? Value { get; set; }
    public string Unit { get; set; } = "";
    public MetricStatus Status { get; set; } = MetricStatus.NOT_AVAILABLE;
    public string Message { get; set; } = "";
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

public enum MetricStatus
{
    OK,
    NOT_AVAILABLE,
    TIMEOUT,
    ERROR,
}
