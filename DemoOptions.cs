internal sealed class DemoOptions
{
    private const string DefaultInput = "Open https://www.bing.com with the browser-automation skill, take a screenshot, verify it exists, and report the exact path.";

    public Uri BaseUrl { get; init; } = new("https://luminaapi-eastus2.sdf.copilotlumina.com");

    public string Partner { get; init; } = "CompliantSydney";

    public string ScenarioGroup { get; init; } = "Mainline";

    public string Scenario { get; init; } = "CodingHarnessPilot";

    public string SandboxId { get; init; } = $"skills-agent-{Guid.NewGuid()}";

    public string ConversationId { get; init; } = Guid.NewGuid().ToString();

    public string? AgentType { get; init; }

    public string? Model { get; init; }

    public string? ModelType { get; init; }

    public string? OverwriteModelKey { get; init; }

    public string Input { get; init; } = DefaultInput;

    public string? BearerToken { get; init; }

    public string? TokenFile { get; init; }

    public string OutputDirectory { get; init; } = Path.Combine("artifacts", "screenshots");

    public string? StreamLogPath { get; init; }

    public string? RunLogPath { get; init; }

    public bool KeepSandbox { get; init; }

    public bool TakeControl { get; init; }

    public bool Verbose { get; init; }

    public bool ShowHelp { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(100);

    public TimeSpan SseReadTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public Uri AcsHelperBaseUrl { get; init; } = new("https://acs-rp.azurewebsites.net/public/releases/CL2025.R20/stable/1.37.2/");

    public static DemoOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{arg}'. Use --help for usage.");
            }

            var key = arg[2..];
            if (key is "help" or "keep-sandbox" or "take-control" or "verbose")
            {
                flags.Add(key);
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for --{key}.");
            }

            values[key] = args[++i];
        }

        var tokenFromEnv = Environment.GetEnvironmentVariable("LUMINA_BEARER_TOKEN");
        var tokenFileFromEnv = Environment.GetEnvironmentVariable("LUMINA_TOKEN_FILE");
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var defaultStreamLogPath = Path.Combine("artifacts", "logs", $"lumina-sdk-stream-chunks-{timestamp}.jsonl");
        var defaultRunLogPath = Path.Combine("artifacts", "logs", $"lumina-sdk-run-{timestamp}.log");

        return new DemoOptions
        {
            BaseUrl = GetUri(values, "endpoint", new Uri("https://luminaapi-eastus2.sdf.copilotlumina.com")),
            Partner = Get(values, "partner", "CompliantSydney")!,
            ScenarioGroup = Get(values, "scenario-group", "Mainline")!,
            Scenario = Get(values, "scenario", "CodingHarnessPilot")!,
            SandboxId = Get(values, "sandbox-id", $"skills-agent-{Guid.NewGuid()}")!,
            ConversationId = Get(values, "conversation-id", Guid.NewGuid().ToString())!,
            AgentType = Get(values, "agent-type", null),
            Model = Get(values, "model", null),
            ModelType = Get(values, "model-type", null),
            OverwriteModelKey = Get(values, "overwrite-model-key", null),
            Input = Get(values, "input", DefaultInput)!,
            BearerToken = Get(values, "token", tokenFromEnv),
            TokenFile = Get(values, "token-file", tokenFileFromEnv),
            OutputDirectory = Get(values, "output", Path.Combine("artifacts", "screenshots"))!,
            StreamLogPath = Get(values, "stream-log", defaultStreamLogPath),
            RunLogPath = Get(values, "run-log", defaultRunLogPath),
            KeepSandbox = flags.Contains("keep-sandbox"),
            TakeControl = flags.Contains("take-control"),
            Verbose = flags.Contains("verbose"),
            ShowHelp = flags.Contains("help"),
            RequestTimeout = TimeSpan.FromSeconds(GetInt(values, "timeout-seconds", 100)),
            SseReadTimeout = TimeSpan.FromSeconds(GetInt(values, "sse-timeout-seconds", 600)),
            AcsHelperBaseUrl = GetUri(values, "acs-helper", new Uri("https://acs-rp.azurewebsites.net/public/releases/CL2025.R20/stable/1.37.2/")),
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Lumina SDK browser automation + Take Control validation demo");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project lumina-browser-take-control-demo -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --endpoint <url>              Default: https://luminaapi-eastus2.sdf.copilotlumina.com");
        Console.WriteLine("  --partner <name>              Default: CompliantSydney");
        Console.WriteLine("  --scenario-group <name>       Default: Mainline");
        Console.WriteLine("  --scenario <name>             Default: CodingHarnessPilot");
        Console.WriteLine("  --sandbox-id <id>             Default: skills-agent-<guid>");
        Console.WriteLine("  --agent-type <type>           Optional metadata override, e.g. ghc");
        Console.WriteLine("  --model <name>                Optional metadata override, e.g. dev-gpt-55-reasoning");
        Console.WriteLine("  --model-type <type>           Optional metadata override, e.g. openai");
        Console.WriteLine("  --overwrite-model-key <key>   Optional overwriteModels key, e.g. GHC_MODEL");
        Console.WriteLine("  --input <text>                Agent input prompt sent as the A2A message description");
        Console.WriteLine("  --token <jwt>                 Or set LUMINA_BEARER_TOKEN");
        Console.WriteLine("  --token-file <path>           Or set LUMINA_TOKEN_FILE");
        Console.WriteLine("  --output <dir>                Default: artifacts/screenshots");
        Console.WriteLine("  --stream-log <path>           Write every SDK stream chunk as JSON Lines; default: artifacts/logs/lumina-sdk-stream-chunks-<timestamp>.jsonl");
        Console.WriteLine("  --run-log <path>              Tee console output and errors; default: artifacts/logs/lumina-sdk-run-<timestamp>.log");
        Console.WriteLine("  --take-control                Also create ACS users/room and call take/release through SDK Desktop API");
        Console.WriteLine("  --acs-helper <url>            Default: public CL2025.R20 stable helper");
        Console.WriteLine("  --keep-sandbox                Do not close the sandbox in cleanup");
        Console.WriteLine("  --verbose                     Print SDK diagnostics and exception details");
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key, string? fallback)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static Uri GetUri(IReadOnlyDictionary<string, string> values, string key, Uri fallback)
    {
        var value = Get(values, key, null);
        return value is null ? fallback : new Uri(value.TrimEnd('/') + "/");
    }

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
    {
        return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
