using Microsoft.Lumina.Common.Models.A2A;
using Newtonsoft.Json.Linq;

internal static class PartMetadata
{
    public static bool IsType(Part part, string expectedType)
    {
        var metadataType = part.Metadata?.TryGetValue("type", out var token) == true
            ? token.ToString()
            : null;
        return string.Equals(metadataType, expectedType, StringComparison.OrdinalIgnoreCase);
    }

    public static bool DataTypeEquals(Dictionary<string, JToken> data, string expectedType)
    {
        var dataType = data.TryGetValue("type", out var token)
            ? token.ToString()
            : null;
        return string.Equals(dataType, expectedType, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Equals(Part part, string key, string expectedValue)
    {
        var value = part.Metadata?.TryGetValue(key, out var token) == true
            ? token.ToString()
            : null;
        return string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class ScreenshotPath
{
    private const string OutputDir = "/home/oai/share/output/";
    private const string ScreenshotSegment = "/screenshots/";
    private static readonly HashSet<string> ScreenshotExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
    };

    public static bool TryAdd(string? filePath, SortedSet<string> paths)
    {
        return IsScreenshotPath(filePath) && paths.Add(filePath!);
    }

    private static bool IsScreenshotPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)
            || !filePath.StartsWith(OutputDir, StringComparison.Ordinal)
            || !filePath.Contains(ScreenshotSegment, StringComparison.Ordinal))
        {
            return false;
        }

        return ScreenshotExtensions.Contains(Path.GetExtension(filePath));
    }
}
