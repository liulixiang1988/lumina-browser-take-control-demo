using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Lumina.Api.Client;
using Microsoft.Lumina.Common.Models.A2A;
using Microsoft.Lumina.Contracts.Models.Desktop;
using Microsoft.Lumina.Contracts.Models.FileSystem;
using Microsoft.Lumina.Contracts.Models.Sandbox;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed class LuminaBrowserTakeControlValidator
{
    private readonly ILuminaApiClient client;
    private readonly DemoOptions options;
    private readonly SandboxRegionProvider stateProvider;

    public LuminaBrowserTakeControlValidator(ILuminaApiClient client, DemoOptions options, SandboxRegionProvider stateProvider)
    {
        this.client = client;
        this.options = options;
        this.stateProvider = stateProvider;
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(options.OutputDirectory);

        Console.WriteLine("Lumina SDK browser-automation + Take Control validation demo");
        Console.WriteLine($"Endpoint:   {options.BaseUrl}");
        Console.WriteLine($"Partner:    {options.Partner}/{options.ScenarioGroup}/{options.Scenario}");
        Console.WriteLine($"SandboxId:  {options.SandboxId}");
        Console.WriteLine($"Agent:      {options.AgentType ?? "(server default)"}");
        Console.WriteLine($"Model:      {options.Model ?? "(server default)"}{(options.ModelType is null ? string.Empty : $" ({options.ModelType})")}");
        Console.WriteLine($"Input:      {options.Input}");
        if (!string.IsNullOrWhiteSpace(options.OverwriteModelKey) && !string.IsNullOrWhiteSpace(options.Model))
        {
            Console.WriteLine($"Override:   {options.OverwriteModelKey}={options.Model}");
        }

        Console.WriteLine($"OutputDir:  {Path.GetFullPath(options.OutputDirectory)}");
        if (!string.IsNullOrWhiteSpace(options.StreamLogPath))
        {
            Console.WriteLine($"StreamLog:  {Path.GetFullPath(options.StreamLogPath)}");
        }

        Console.WriteLine(string.Empty);

        try
        {
            await OpenSandboxAsync().ConfigureAwait(false);

            var streamResult = await InvokeSkillsAgentAsync().ConfigureAwait(false);
            PrintStreamSummary(streamResult);

            if (streamResult.ScreenshotPaths.Count == 0)
            {
                throw new InvalidOperationException("No browser-automation file-part screenshot paths were discovered from the SDK Agent stream.");
            }

            await DownloadScreenshotsAsync(streamResult.ScreenshotPaths).ConfigureAwait(false);

            if (options.TakeControl)
            {
                await ExerciseTakeControlAsync().ConfigureAwait(false);
            }

            if (!streamResult.TaskSucceeded)
            {
                throw new InvalidOperationException("Agent stream completed, but final artifact metadata did not report success.");
            }

            Console.WriteLine("Validation completed successfully through the Lumina SDK.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Validation failed: {ex.Message}");
            if (options.Verbose)
            {
                Console.Error.WriteLine(ex);
            }

            return 1;
        }
        finally
        {
            if (!options.KeepSandbox)
            {
                await CloseSandboxBestEffortAsync().ConfigureAwait(false);
            }
            else
            {
                Console.WriteLine($"Keeping sandbox open: {options.SandboxId}");
            }
        }
    }

    private async Task OpenSandboxAsync()
    {
        Console.WriteLine("[Step 1] Opening sandbox through SDK Sandboxes.OpenSandboxAsync...");
        using var scope = new LuminaRequestScope();
        var sandbox = await client.Sandboxes.OpenSandboxAsync(options.SandboxId, new OpenRequest()).ConfigureAwait(false);
        PrintScope("OpenSandbox", scope);
        Console.WriteLine($"[Step 1] Sandbox opened. region={stateProvider.SandboxRegion ?? "(not reported)"} computerId={GetComputerId(sandbox)}");
    }

    private string GetComputerId(SandboxResponse sandbox)
    {
        return GetSandboxProperty(sandbox, "computerId") ?? sandbox.SandboxId ?? options.SandboxId;
    }

    private static string? GetSandboxProperty(SandboxResponse sandbox, string key)
    {
        if (sandbox.Properties is null)
        {
            return null;
        }

        return sandbox.Properties.TryGetValue(key, out var value)
            || sandbox.Properties.TryGetValue(char.ToUpperInvariant(key[0]) + key[1..], out value)
            ? value
            : null;
    }

    private async Task<AgentStreamResult> InvokeSkillsAgentAsync()
    {
        Console.WriteLine("[Step 2] Invoking skills-agent through SDK Agent.SendAndStreamWithResubscribeAsync...");
        var request = BuildAgentRequest();
        var result = new AgentStreamResult();
        var collector = new AgentStreamResultCollector(result);
        StreamWriter? streamLog = null;
        var streamChunkIndex = 0;
        var streamLogFullPath = string.IsNullOrWhiteSpace(options.StreamLogPath)
            ? null
            : Path.GetFullPath(options.StreamLogPath);

        try
        {
            streamLog = CreateStreamLogWriter();
            if (streamLog is not null && streamLogFullPath is not null)
            {
                Console.WriteLine($"[StreamLog] Writing every SDK stream chunk to {streamLogFullPath}");
            }

            using var scope = new LuminaRequestScope();
            await foreach (var chunk in client.Agent.SendAndStreamWithResubscribeAsync(
                options.SandboxId,
                request,
                maxResubscribeAttempts: 3,
                logFunc: options.Verbose ? Console.WriteLine : null).ConfigureAwait(false))
            {
                streamChunkIndex++;
                if (streamLog is not null)
                {
                    await LogStreamChunkAsync(streamLog, streamChunkIndex, chunk).ConfigureAwait(false);
                }

                if (chunk.Error is not null)
                {
                    throw new InvalidOperationException($"A2A error {chunk.Error.Code}: {chunk.Error.Message}");
                }

                if (chunk.Result is TaskArtifactUpdateEvent artifactUpdate)
                {
                    collector.Collect(artifactUpdate);
                    if (artifactUpdate.LastChunk == true)
                    {
                        result.LastChunkSeen = true;
                        if (!result.TaskStatusReported)
                        {
                            result.TaskSucceeded = true;
                        }

                        break;
                    }
                }
            }

            PrintScope("AgentStream", scope);
        }
        finally
        {
            if (streamLog is not null)
            {
                await streamLog.DisposeAsync().ConfigureAwait(false);
            }
        }

        Console.WriteLine($"[Step 2] Stream complete. lastChunk={result.LastChunkSeen}, taskSucceeded={result.TaskSucceeded}");
        if (streamLogFullPath is not null)
        {
            Console.WriteLine($"[StreamLog] Recorded {streamChunkIndex} SDK stream chunks.");
            Console.WriteLine($"[StreamLog] JSONL path: {streamLogFullPath}");
        }

        return result;
    }

    private StreamWriter? CreateStreamLogWriter()
    {
        if (string.IsNullOrWhiteSpace(options.StreamLogPath))
        {
            return null;
        }

        var parent = Path.GetDirectoryName(options.StreamLogPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        return new StreamWriter(options.StreamLogPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static Task LogStreamChunkAsync(StreamWriter writer, int sequence, object chunk)
    {
        var json = JsonConvert.SerializeObject(
            new
            {
                sequence,
                receivedAt = DateTimeOffset.UtcNow,
                resultType = chunk.GetType().FullName,
                chunk,
            },
            Formatting.None);

        return writer.WriteLineAsync(json);
    }

    private async Task DownloadScreenshotsAsync(IEnumerable<string> paths)
    {
        Console.WriteLine("[Step 3] Downloading screenshots through SDK FileSystem.DownloadAsync...");
        foreach (var path in paths)
        {
            using var scope = new LuminaRequestScope();
            var response = await client.FileSystem.DownloadAsync(
                options.SandboxId,
                new DownloadRequest { FilePath = path }).ConfigureAwait(false);
            PrintScope("FileSystem.Download", scope);

            var localFile = Path.Combine(options.OutputDirectory, Path.GetFileName(path));
            await File.WriteAllBytesAsync(localFile, Convert.FromBase64String(response.Content)).ConfigureAwait(false);
            Console.WriteLine($"[Step 3] Downloaded {path} -> {localFile} ({response.MimeType}, {response.FileSize} bytes)");
        }
    }

    private async Task ExerciseTakeControlAsync()
    {
        Console.WriteLine("[Step 4] Provisioning ACS users and room...");
        var browserUser = await CreateCommunicationUserAsync("browser").ConfigureAwait(false);
        var participantUser = await CreateCommunicationUserAsync("participant").ConfigureAwait(false);
        var roomId = await CreateRoomAsync(browserUser.UserId, participantUser.UserId).ConfigureAwait(false);

        Console.WriteLine($"[Step 4] Taking desktop control with SDK Desktop.TakeControlSessionAsync and ACS room {roomId}...");
        using (var scope = new LuminaRequestScope())
        {
            await client.Desktop.TakeControlSessionAsync(
                options.SandboxId,
                new TakeControlSessionRequest
                {
                    RoomId = roomId,
                    BrowserUserToken = browserUser.Token,
                    BrowserUserId = browserUser.UserId,
                }).ConfigureAwait(false);
            PrintScope("Desktop.TakeControlSession", scope);
        }

        Console.WriteLine("[Step 4] Releasing desktop control with SDK Desktop.ReleaseControlSessionAsync...");
        using (var scope = new LuminaRequestScope())
        {
            await client.Desktop.ReleaseControlSessionAsync(
                options.SandboxId,
                new ReleaseControlSessionRequest { RoomId = roomId }).ConfigureAwait(false);
            PrintScope("Desktop.ReleaseControlSession", scope);
        }

        Console.WriteLine("[Step 4] Take Control API validation complete.");
    }

    private async Task<AcsUser> CreateCommunicationUserAsync(string label)
    {
        using var http = new HttpClient { BaseAddress = options.AcsHelperBaseUrl, Timeout = options.RequestTimeout };
        using var response = await http.PostAsync("getCommunicationUserToken", JsonContent.Create(new { })).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var root = document.RootElement;

        var token = root.GetProperty("communicationUserToken").GetProperty("token").GetString();
        var userIdElement = root.GetProperty("userId");
        var userId = userIdElement.ValueKind == JsonValueKind.Object
            ? userIdElement.GetProperty("communicationUserId").GetString()
            : userIdElement.GetString();

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException($"ACS helper returned incomplete {label} user payload.");
        }

        Console.WriteLine($"[ACS] {label} user: {userId}");
        return new AcsUser(token, userId);
    }

    private async Task<string> CreateRoomAsync(string browserUserId, string participantUserId)
    {
        using var http = new HttpClient { BaseAddress = options.AcsHelperBaseUrl, Timeout = options.RequestTimeout };
        using var response = await http.PostAsJsonAsync("createRoom", new { presenterUserIds = new[] { browserUserId, participantUserId } }).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var roomId = document.RootElement.GetProperty("roomId").GetString();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new InvalidOperationException("ACS helper did not return roomId.");
        }

        Console.WriteLine($"[ACS] room: {roomId}");
        return roomId;
    }

    private async Task CloseSandboxBestEffortAsync()
    {
        try
        {
            Console.WriteLine("[Cleanup] Closing sandbox through SDK Sandboxes.CloseSandboxAsync...");
            using var scope = new LuminaRequestScope();
            await client.Sandboxes.CloseSandboxAsync(options.SandboxId).ConfigureAwait(false);
            PrintScope("CloseSandbox", scope);
            Console.WriteLine("[Cleanup] Sandbox closed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cleanup] Close failed: {ex.Message}");
        }
    }

    private A2ARequest BuildAgentRequest()
    {
        var skillsList = JArray.FromObject(new[]
        {
            new { name = "web-artifacts-builder" },
            new { name = "ppt" },
            new { name = "docx" },
            new { name = "xlsx" },
            new { name = "brand-guidelines" },
            new { name = "canvas-design" },
            new { name = "internal-comms" },
            new { name = "pdf" },
            new { name = "slack-gif-creator" },
            new { name = "infographic-gen" },
            new { name = "skill-register" },
            new { name = "browser-automation" },
        });
        var metadata = new Dictionary<string, JToken>
        {
            ["skillsList"] = skillsList,
        };
        AddMetadataString(metadata, "agentType", options.AgentType);
        AddMetadataString(metadata, "model", options.Model);
        AddMetadataString(metadata, "modelType", options.ModelType);
        if (!string.IsNullOrWhiteSpace(options.OverwriteModelKey) && !string.IsNullOrWhiteSpace(options.Model))
        {
            metadata["overwriteModels"] = JObject.FromObject(new Dictionary<string, string>
            {
                [options.OverwriteModelKey] = options.Model,
            });
        }

        return new A2ARequest
        {
            Jsonrpc = "2.0",
            Id = 1,
            Method = "message/stream",
            Params = new A2ARequestParams
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.User,
                    Parts = new List<Part>
                    {
                        new DataPart
                        {
                            Data = new Dictionary<string, JToken>
                            {
                                ["description"] = options.Input,
                            },
                        },
                    },
                    MessageId = Guid.NewGuid().ToString(),
                },
                Lumina = new Dictionary<string, string>
                {
                    ["agent_name"] = "skills-agent",
                },
                Metadata = metadata,
            },
        };
    }

    private static void AddMetadataString(Dictionary<string, JToken> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private static void PrintStreamSummary(AgentStreamResult result)
    {
        Console.WriteLine("[Summary]");
        Console.WriteLine($"  lastChunk:       {result.LastChunkSeen}");
        Console.WriteLine($"  taskSucceeded:   {result.TaskSucceeded}");
        Console.WriteLine($"  finalSubtype:    {result.FinalSubtype ?? "(none)"}");
        Console.WriteLine($"  screenshots:     {result.ScreenshotPaths.Count}");
        foreach (var path in result.ScreenshotPaths)
        {
            Console.WriteLine($"    - {path}");
        }

        Console.WriteLine($"  read paths:      {result.ReadPaths.Count}");
        foreach (var path in result.ReadPaths)
        {
            Console.WriteLine($"    - {path}");
        }

        if (!string.IsNullOrWhiteSpace(result.SessionResultText))
        {
            Console.WriteLine("  session_result:");
            Console.WriteLine(Indent(result.SessionResultText!, "    "));
        }
    }

    private static void PrintScope(string operation, LuminaRequestScope scope)
    {
        Console.WriteLine($"[SDK] {operation}: TraceId={scope.TraceId ?? "(none)"} CorrelationId={scope.CorrelationId ?? "(none)"}");
    }

    private static string Indent(string text, string prefix)
    {
        return string.Join(Environment.NewLine, text.Split('\n').Select(line => prefix + line.TrimEnd('\r')));
    }
}
