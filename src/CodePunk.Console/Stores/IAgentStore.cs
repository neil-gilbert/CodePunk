namespace CodePunk.Console.Stores;

public interface IAgentStore
{
    Task CreateAsync(AgentDefinition definition, bool overwrite = false, CancellationToken ct = default);
    Task<AgentDefinition?> GetAsync(string name, CancellationToken ct = default);
    Task<IEnumerable<AgentDefinition>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
}
