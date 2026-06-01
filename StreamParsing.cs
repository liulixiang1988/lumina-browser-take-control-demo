using Microsoft.Lumina.Common.Models.A2A;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

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
}

internal static class ScreenshotPath
{
    private const string ScreenshotDir = "/home/oai/share/output/screenshots/";
    private static readonly Regex ScreenshotPathPattern = new(
        @"/home/oai/share/output/screenshots/[^\s""'`<>]+?\.(?:png|jpe?g|webp)(?=$|[\s""'`<>\)\]\}\.,;:!?])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> ScreenshotExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
    };

    public static void CaptureFromOutput(string? outputText, SortedSet<string> screenshotPaths)
    {
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return;
        }

        foreach (Match match in ScreenshotPathPattern.Matches(outputText))
        {
            var filePath = match.Value;
            if (TryAdd(filePath, screenshotPaths))
            {
                Console.WriteLine($"[Stream] Screenshot captured: {filePath}");
            }
        }
    }

    public static bool TryAdd(string? filePath, SortedSet<string> paths)
    {
        return IsScreenshotPath(filePath) && paths.Add(filePath!);
    }

    private static bool IsScreenshotPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !filePath.StartsWith(ScreenshotDir, StringComparison.Ordinal))
        {
            return false;
        }

        return ScreenshotExtensions.Contains(Path.GetExtension(filePath));
    }
}

internal static class JsonPayloadExtractor
{
    public static bool TryParse(string content, out JObject payload)
    {
        if (!TryExtractObject(content, out var json))
        {
            payload = new JObject();
            return false;
        }

        try
        {
            payload = JObject.Parse(json);
            return true;
        }
        catch (JsonReaderException)
        {
            payload = new JObject();
            return false;
        }
    }

    private static bool TryExtractObject(string content, out string json)
    {
        json = string.Empty;
        var start = content.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < content.Length; i++)
        {
            var current = content[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = content[start..(i + 1)];
                    return true;
                }
            }
        }

        return false;
    }
}
