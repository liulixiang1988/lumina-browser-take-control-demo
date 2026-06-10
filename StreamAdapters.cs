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

internal sealed class BrowserAutomationFileAdapter : IAgentPartAdapter
{
    public bool TryCollect(Part part, AgentStreamResult result)
    {
        if (part is not FilePart filePart
            || !PartMetadata.ValueEquals(part.Metadata, "source", "browser-automation")
            || !PartMetadata.ValueEquals(part.Metadata, "eventType", "add")
            || filePart.File?.Uri is null)
        {
            return false;
        }

        var uri = filePart.File.Uri.ToString();
        if (OutputImagePath.TryAdd(uri, result.BrowserAutomationFilePaths))
        {
            Console.WriteLine($"[Stream] Browser automation file: {uri}");
        }

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

internal sealed class ClaudeCodeToolResultAdapter : IAgentPartAdapter
{
    public bool TryCollect(Part part, AgentStreamResult result)
    {
        if (part is not DataPart dataPart
            || dataPart.Data is null
            || !PartMetadata.IsType(part, "tool_result")
            || !PartMetadata.DataTypeEquals(dataPart.Data, "user.message.tool_result"))
        {
            return false;
        }

        if (dataPart.Data.TryGetValue("is_error", out var isError) && isError.Type == JTokenType.Boolean && isError.Value<bool>())
        {
            return true;
        }

        if (dataPart.Data.TryGetValue("content", out var content)
            && content.Type == JTokenType.String
            && JsonPayloadExtractor.TryParse(content.ToString(), out var payload))
        {
            ScreenshotPath.CaptureFromOutput(payload.Value<string>("output"), result.ScreenshotPaths);
        }

        return true;
    }
}

internal sealed class GhcToolExecutionAdapter : IAgentPartAdapter
{
    public bool TryCollect(Part part, AgentStreamResult result)
    {
        if (part is not DataPart dataPart
            || dataPart.Data is null
            || !PartMetadata.DataTypeEquals(dataPart.Data, "tool.execution_complete"))
        {
            return false;
        }

        if (dataPart.Data.TryGetValue("success", out var success) && success.Type == JTokenType.Boolean && !success.Value<bool>())
        {
            return true;
        }

        var content = GetToolExecutionContent(dataPart.Data);
        if (content is not null
            && JsonPayloadExtractor.TryParse(content, out var payload)
            && payload.Value<bool?>("success") == true)
        {
            ScreenshotPath.CaptureFromOutput(payload.Value<string>("output"), result.ScreenshotPaths);
        }

        return true;
    }

    private static string? GetToolExecutionContent(Dictionary<string, JToken> data)
    {
        if (data.TryGetValue("content", out var content) && content.Type == JTokenType.String)
        {
            return content.ToString();
        }

        if (data.TryGetValue("result", out var result) && result is JObject resultObject)
        {
            return resultObject.Value<string>("content") ?? resultObject.Value<string>("detailedContent");
        }

        return null;
    }
}
