# Lumina Browser Automation + Take Control Demo

SDK-based .NET 8 console demo for validating the browser-automation and optional Take Control flow from PR 5243015.

The demo uses the real `Microsoft.Lumina` SDK package plus the SDK sample patterns from the Lumina API Demo / .NET partner documentation:

- `ServiceCollection.AddLuminaApiClient()`
- `ILuminaApiClientFactory.Create(...)`
- `client.Sandboxes.OpenSandboxAsync(...)` / `CloseSandboxAsync(...)`
- `client.Agent.SendAndStreamWithResubscribeAsync(...)`
- `client.FileSystem.DownloadAsync(...)`
- optional `client.Desktop.TakeControlSessionAsync(...)` / `ReleaseControlSessionAsync(...)`

The validation flow keeps the Lumina SDK calls visible while separating the stream result adapters:

1. Open a sandbox.
2. Send a `skills-agent` A2A `message/stream` request through the SDK Agent API with the production `skillsList` and `browser-automation` enabled.
3. Optionally write every SDK stream chunk to JSONL as it is returned by `SendAndStreamWithResubscribeAsync(...)`.
4. Collect screenshot paths through separate stream adapters for Claude Code and GHC result shapes.
5. Download screenshots through SDK `FileSystem.DownloadAsync` while the sandbox is alive.
6. Optionally create ACS users/room and call SDK `Desktop.TakeControlSessionAsync` / `ReleaseControlSessionAsync`.
7. Close the sandbox unless `--keep-sandbox` is set.

| Flow step | Lumina SDK call | Data retained | Claude Code result shape | GHC result shape |
| --- | --- | --- | --- | --- |
| Open sandbox | `client.Sandboxes.OpenSandboxAsync(...)` | Trace/correlation IDs printed in the run log | Same | Same |
| Invoke browser automation | `client.Agent.SendAndStreamWithResubscribeAsync(...)` | Optional `--stream-log` JSONL, one SDK-returned chunk per line | Tool result uses `metadata.type: "tool_result"`, `data.type: "user.message.tool_result"`, and JSON `content.output: "Screenshot saved: <path>"` | Tool completion uses `data.type: "tool.execution_complete"` and JSON under `result.content` / `result.detailedContent`; output can include trailing process text |
| Verify downloadable images | Stream result adapters in the demo | Screenshot, browser-automation file, and optional `Read` verification paths printed in summary | `ClaudeCodeToolResultAdapter` extracts `Screenshot saved: /home/oai/share/output/.../<file>`; `BrowserAutomationFileAdapter` also collects `source: browser-automation` file `Uri` values | `GhcToolExecutionAdapter` extracts successful tool output paths; `BrowserAutomationFileAdapter` collects browser screenshots emitted as file parts |
| Download images | `client.FileSystem.DownloadAsync(...)` | Local file under `artifacts/screenshots` by default | Same | Same |
| Optional Take Control | `client.Desktop.TakeControlSessionAsync(...)` / `ReleaseControlSessionAsync(...)` | Trace/correlation IDs printed in the run log | Same | Same |

No `customMetaPrompt` is sent. The current Skills Agent default behavior is expected to save screenshots under `/home/oai/share/output/screenshots/`.

## Code map

Start with `Program.cs` for CLI startup, token loading, SDK client creation, and endpoint safety checks. The main SDK flow lives in `LuminaBrowserTakeControlValidator.cs`, so the open sandbox, stream agent, download screenshot, optional Take Control, and cleanup calls remain easy to read in order.

Stream result handling is split at the adapter seam:

| File | Module | What to read there |
| --- | --- | --- |
| `AgentStreamResultCollector.cs` | `AgentStreamResultCollector` | Reads artifact metadata and dispatches each stream part to adapters. |
| `StreamAdapters.cs` | `BrowserAutomationFileAdapter`, `ClaudeCodeToolResultAdapter`, `GhcToolExecutionAdapter`, plus session/read adapters | Keeps browser file artifacts, Claude Code results, and GHC result parsing side by side without mixing their formats. |
| `StreamParsing.cs` | `PartMetadata`, `ScreenshotPath`, `JsonPayloadExtractor` | Shared parsing helpers for metadata type checks, screenshot path validation, and JSON embedded in tool output. |
| `AgentStreamResult.cs` | `AgentStreamResult` | The accumulated stream state used by the SDK flow. |
| `DemoOptions.cs` / `TokenLoader.cs` / `DemoTypes.cs` | CLI options, token loading, small value types | Supporting modules kept out of the SDK flow. |

## Run

Set a Lumina bearer token:

```powershell
$env:LUMINA_BEARER_TOKEN = "<token>"
```

Run the default SDF + CompliantSydney validation:

```powershell
dotnet run --project .\lumina-browser-take-control-demo
```

Pass a custom agent input prompt from the command line:

```powershell
dotnet run --project .\lumina-browser-take-control-demo -- `
  --input "Open https://www.bing.com, take a screenshot, and report the exact screenshot path."
```

By default, the request omits agent/model metadata and lets the service use its deployed default agent backend, which is normally Claude Code (`cc`). To explicitly select the GHC runner with the OpenAI reasoning model, pass this request metadata:

```json
{
  "agentType": "ghc",
  "model": "dev-gpt-55-reasoning",
  "modelType": "openai",
  "overwriteModels": {
    "GHC_MODEL": "dev-gpt-55-reasoning"
  }
}
```

Equivalent explicit run with endpoint and model metadata:

```powershell
dotnet run --project .\lumina-browser-take-control-demo -- `
  --endpoint https://luminaapi-eastus2.sdf.copilotlumina.com `
  --partner CompliantSydney `
  --scenario-group Mainline `
  --scenario CodingHarnessPilot `
  --agent-type ghc `
  --model dev-gpt-55-reasoning `
  --model-type openai `
  --overwrite-model-key GHC_MODEL
```

Run with the GHC runner while leaving `model`, `modelType`, and `overwriteModels` empty so the service resolves its default model:

```powershell
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$streamLog = ".\artifacts\logs\lumina-sdk-ghc-cc-default-stream-chunks-$timestamp.jsonl"
$runLog = ".\artifacts\logs\lumina-sdk-ghc-cc-default-run-$timestamp.log"

New-Item -ItemType Directory -Force -Path .\artifacts\logs | Out-Null
dotnet build .\lumina-browser-take-control-demo\LuminaBrowserTakeControlDemo.csproj --verbosity quiet
.\lumina-browser-take-control-demo\bin\Debug\net8.0\LuminaBrowserTakeControlDemo.exe `
  --agent-type ghc `
  --stream-log $streamLog `
  --run-log $runLog
```

In SDF this path reached the GHC runner and used the service default Claude model (`prod-anthropic-claude-opus-4-6`). The demo handles GHC `tool.execution_complete` chunks separately from Claude Code `tool_result` chunks so both result shapes can produce the screenshot path.

Use a token file instead of an environment variable:

```powershell
dotnet run --project .\lumina-browser-take-control-demo -- --token-file C:\path\to\auth_token.txt
```

Exercise the EPS Take Control endpoints after browser automation succeeds:

```powershell
dotnet run --project .\lumina-browser-take-control-demo -- --take-control
```

The Take Control mode provisions two ACS users and one ACS Room through the helper URL, then validates the SDK Desktop API calls. It does not join the ACS Room with an external WebRTC participant.

Record every `Lumina SDK` stream chunk returned by `client.Agent.SendAndStreamWithResubscribeAsync(...)`:

```powershell
$ErrorActionPreference = "Stop"
$tokenScript = "C:\path\to\CopilotLumina\sources\dev\SandboxService\AIAgents\ts-agents\egress-llm\scripts\get-lumina-token.ts"
$env:PATH = "$env:USERPROFILE\.bun\bin;$env:PATH"

$tokenOutput = & bun $tokenScript 2>&1 | ForEach-Object { $_.ToString() }
$tokenIndex = [Array]::IndexOf($tokenOutput, "Your access token:")
if ($tokenIndex -lt 0 -or $tokenIndex + 1 -ge $tokenOutput.Count) {
  throw "Could not parse token from helper output."
}

$env:LUMINA_BEARER_TOKEN = $tokenOutput[$tokenIndex + 1].Trim()
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$streamLog = ".\artifacts\logs\lumina-sdk-stream-chunks-$timestamp.jsonl"
$runLog = ".\artifacts\logs\lumina-sdk-run-with-stream-log-$timestamp.log"

New-Item -ItemType Directory -Force -Path .\artifacts\logs | Out-Null
dotnet build .\lumina-browser-take-control-demo\LuminaBrowserTakeControlDemo.csproj --verbosity quiet
.\lumina-browser-take-control-demo\bin\Debug\net8.0\LuminaBrowserTakeControlDemo.exe `
  --stream-log $streamLog `
  --run-log $runLog
```

The JSONL file records the SDK-returned chunks, one JSON object per line, after the Lumina SDK has handled the underlying REST/SSE call. It is not a direct REST API capture. The demo always writes and prints both `[StreamLog] JSONL path: ...` and `[RunLog] Log path: ...`; override the paths with `--stream-log` and `--run-log` when needed. Keep the JSONL with the run log when debugging or comparing Claude Code versus GHC output; generated logs stay local under `artifacts/logs` unless you explicitly choose to share them.

If the GHC run returns `Resource not found on provider ... (HTTP 404)`, the request metadata reached the GHC runner but the selected model is not available from the current egress-llm provider.

## Options

```text
--endpoint <url>           Default: https://luminaapi-eastus2.sdf.copilotlumina.com
--partner <name>           Default: CompliantSydney
--scenario-group <name>    Default: Mainline
--scenario <name>          Default: CodingHarnessPilot
--sandbox-id <id>          Default: skills-agent-<guid>
--agent-type <type>        Optional metadata override, e.g. ghc
--model <name>             Optional metadata override, e.g. dev-gpt-55-reasoning
--model-type <type>        Optional metadata override, e.g. openai
--overwrite-model-key <k>  Optional overwriteModels key, e.g. GHC_MODEL
--input <text>             Agent input prompt sent as the A2A message description
--token <jwt>              Or set LUMINA_BEARER_TOKEN
--token-file <path>        Or set LUMINA_TOKEN_FILE
--output <dir>             Default: artifacts/screenshots
--stream-log <path>        Write every Lumina SDK stream chunk as JSON Lines; default: artifacts/logs/lumina-sdk-stream-chunks-<timestamp>.jsonl
--run-log <path>           Tee console output and errors; default: artifacts/logs/lumina-sdk-run-<timestamp>.log
--take-control             Also create ACS users/room and call SDK Desktop take/release
--acs-helper <url>         Default: public CL2025.R20 stable helper
--keep-sandbox             Do not close the sandbox in cleanup
--verbose                  Print exception details
```

## Notes

- The project consumes `Microsoft.Lumina` from the Enzyme NuGet feed configured in the repo-level `NuGet.config`.
- The client refuses to send a bearer token to hosts outside `*.copilotlumina.com` or localhost.
- Region affinity is handled through SDK `SandboxStateProvider`.
- Trace and correlation IDs are printed through SDK `LuminaRequestScope`.
- Screenshots are saved locally under `artifacts/screenshots` by default.