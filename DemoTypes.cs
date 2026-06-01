using Microsoft.Lumina.Api.Client;

internal sealed class SandboxRegionProvider : ILuminaSandboxStateProvider
{
    public string? SandboxRegion { get; set; }
}

internal sealed record AcsUser(string Token, string UserId);
