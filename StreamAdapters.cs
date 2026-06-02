using Microsoft.Lumina.Common.Models.A2A;
using Newtonsoft.Json.Linq;

internal interface IAgentPartAdapter
{
    bool TryCollect(Part part, AgentStreamResult result);
}

internal sealed class SessionResultAdapter : IAgentPartAdapter
{
    public bool TryCollect(Part part, AgentStreamResult result)
    {
        if (part is not TextPart textPart || !PartMetadata.IsType(part, "session_result"))
        {
            return false;
        }

        result.SessionResultText = textPart.Text;
        return true;
    }
}

internal sealed class ReadVerificationAdapter : IAgentPartAdapter
{
    public bool TryCollect(Part part, AgentStreamResult result)
    {
        if (part is not DataPart dataPart
            || dataPart.Data is null
            || !PartMetadata.IsType(part, "tool_call")
            || !PartMetadata.DataTypeEquals(dataPart.Data, "assistant.message.tool_use")
            || !dataPart.Data.TryGetValue("name", out var name)
            || !string.Equals(name.ToString(), "Read", StringComparison.Ordinal)
            || !dataPart.Data.TryGetValue("input", out var input)
            || input is not JObject inputObject)
        {
            return false;
        }

        var filePath = inputObject.Value<string>("file_path");
        if (ScreenshotPath.TryAdd(filePath, result.ReadPaths))
        {
            Console.WriteLine($"[Stream] Read verification path: {filePath}");
        }

        return true;
    }
}

internal sealed class BrowserAutomationFilePartAdapter : IAgentPartAdapter
{
    public bool TryCollect(Part part, AgentStreamResult result)
    {
        if (part is not FilePart filePart
            || filePart.File is null
            || !PartMetadata.Equals(part, "source", "browser-automation")
            || !PartMetadata.Equals(part, "eventType", "add")
            || !IsImageMimeType(filePart.File.MimeType))
        {
            return false;
        }

        var filePath = filePart.File.Uri?.ToString();
        if (ScreenshotPath.TryAdd(filePath, result.ScreenshotPaths))
        {
            Console.WriteLine($"[Stream] Browser automation screenshot artifact: {filePath}");
        }

        return true;
    }

    private static bool IsImageMimeType(string? mimeType)
    {
        return mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    }
}
