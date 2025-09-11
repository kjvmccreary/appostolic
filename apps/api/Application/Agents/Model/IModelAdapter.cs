namespace Appostolic.Api.Application.Agents.Model;

public interface IModelAdapter
{
    Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct);
}
