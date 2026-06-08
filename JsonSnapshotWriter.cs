using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsDeviceJsonCollector;

public sealed class JsonSnapshotWriter
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Write(string? outputFile, AgentSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(snapshot, _options);

        if (string.IsNullOrWhiteSpace(outputFile))
        {
            Console.WriteLine(json);
            return;
        }

        string? directory = Path.GetDirectoryName(outputFile);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputFile, json, Encoding.UTF8);
    }
}
