using Microsoft.Lumina.Common.Models.A2A;
using Newtonsoft.Json.Linq;

internal sealed class AgentStreamResultCollector
{
    private readonly AgentStreamResult result;
    private readonly IAgentPartAdapter[] adapters;

    public AgentStreamResultCollector(AgentStreamResult result)
    {
        this.result = result;
        adapters = new IAgentPartAdapter[]
        {
            new SessionResultAdapter(),
            new BrowserAutomationFilePartAdapter(),
            new ReadVerificationAdapter(),
        };
    }

    public void Collect(TaskArtifactUpdateEvent artifactUpdate)
    {
        ReadArtifactMetadata(artifactUpdate.Artifact);
        foreach (var part in artifactUpdate.Artifact.Parts)
        {
            foreach (var adapter in adapters)
            {
                if (adapter.TryCollect(part, result))
                {
                    break;
                }
            }
        }
    }

    private void ReadArtifactMetadata(Artifact artifact)
    {
        if (artifact.Metadata is null)
        {
            return;
        }

        if (artifact.Metadata.TryGetValue("subtype", out var subtype) && subtype.Type == JTokenType.String)
        {
            result.FinalSubtype = subtype.ToString();
        }

        if (artifact.Metadata.TryGetValue("is_error", out var isError) && isError.Type == JTokenType.Boolean)
        {
            result.TaskStatusReported = true;
            result.TaskSucceeded = !isError.Value<bool>();
        }
    }
}
