using System.Text;

namespace WindowsDeviceJsonCollector;

public class Program
{
    private const string AppVersion = "1.0.0";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        CollectorOptions options = CollectorOptions.Parse(args);
        var writer = new JsonSnapshotWriter();

        using var collector = new HardwareCollector(AppVersion);

        try
        {
            AgentSnapshot snapshot = collector.Collect(options);
            writer.Write(options.OutputFile, snapshot);
            return 0;
        }
        catch (Exception ex)
        {
            AgentSnapshot errorSnapshot = HardwareCollector.CreateErrorSnapshot(
                AppVersion,
                options.AgentId,
                options.Sequence,
                ex
            );

            writer.Write(options.OutputFile, errorSnapshot);
            return 1;
        }
    }
}
