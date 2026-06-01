using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Lumina.Api.Client;
using Microsoft.Lumina.Api.Client.DependencyInjection;
using Microsoft.Lumina.Common.Contracts.Realtime;

var options = DemoOptions.Parse(args);
if (options.ShowHelp)
{
    DemoOptions.PrintHelp();
    return 0;
}

using var consoleLog = ConsoleLogScope.Start(options.RunLogPath);
if (consoleLog is not null)
{
    Console.WriteLine($"[RunLog] Writing console output to {consoleLog.FullPath}");
}

ValidateEndpoint(options.BaseUrl);
var token = await TokenLoader.LoadAsync(options).ConfigureAwait(false);

var services = new ServiceCollection();
services.AddLuminaApiClient();
services.AddHttpClient("LuminaApiClient", client =>
{
    client.DefaultRequestVersion = HttpVersion.Version20;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
})
.UseSocketsHttpHandler((handler, _) =>
{
    handler.ConnectTimeout = TimeSpan.FromSeconds(5);
    handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always;
    handler.KeepAlivePingDelay = TimeSpan.FromSeconds(10);
    handler.KeepAlivePingTimeout = TimeSpan.FromSeconds(5);
    handler.EnableMultipleHttp2Connections = true;
});

await using var serviceProvider = services.BuildServiceProvider();
var clientFactory = serviceProvider.GetRequiredService<ILuminaApiClientFactory>();
var stateProvider = new SandboxRegionProvider();
var client = clientFactory.Create(new LuminaApiClientOptions
{
    ServiceBaseAddress = options.BaseUrl,
    AuthorizationTokenProvider = () => Task.FromResult(token),
    RequestContext = new LuminaRequestContext
    {
        Partner = options.Partner,
        ScenarioGroup = options.ScenarioGroup,
        ScenarioName = options.Scenario,
        ConversationId = options.ConversationId,
        RequestId = Guid.NewGuid().ToString(),
    },
    LogFunc = options.Verbose ? Console.WriteLine : null,
    RequestTimeout = options.RequestTimeout,
    SseReadTimeout = options.SseReadTimeout,
    RetryCount = 1,
    HttpClientName = "LuminaApiClient",
    SandboxStateProvider = stateProvider,
    RealtimeOptions = new RealtimeConnectionOptions
    {
        ConnectionTimeout = TimeSpan.FromSeconds(100),
        KeepAliveInterval = TimeSpan.FromSeconds(100),
        ReceiveBufferSize = 32 * 1024,
    },
});

var validator = new LuminaBrowserTakeControlValidator(client, options, stateProvider);
var exitCode = await validator.RunAsync().ConfigureAwait(false);
if (consoleLog is not null)
{
    Console.WriteLine($"[RunLog] Log path: {consoleLog.FullPath}");
}

return exitCode;

static void ValidateEndpoint(Uri baseUrl)
{
    var host = baseUrl.Host;
    if (host.EndsWith(".copilotlumina.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    throw new InvalidOperationException($"Refusing to send a bearer token to unapproved host '{host}'. Use a copilotlumina.com or localhost endpoint.");
}
