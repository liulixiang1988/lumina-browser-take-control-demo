internal sealed class AgentStreamResult
{
    public SortedSet<string> ScreenshotPaths { get; } = new(StringComparer.Ordinal);

    public SortedSet<string> ReadPaths { get; } = new(StringComparer.Ordinal);

    public bool LastChunkSeen { get; set; }

    public bool TaskSucceeded { get; set; }

    public bool TaskStatusReported { get; set; }

    public string? FinalSubtype { get; set; }

    public string? SessionResultText { get; set; }
}
