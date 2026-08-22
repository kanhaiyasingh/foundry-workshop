using Azure.AI.Projects;
using Azure.Core;

namespace FoundryWorkshop.Shared;

public sealed class WorkshopContext : IDisposable
{
    private readonly HttpClient _httpClient = new();

    public WorkshopContext(string labName, string[] args)
    {
        LabName = labName;
        Args = args;
        Config = WorkshopConfig.Load();
        Credential = Config.CreateCredential();
        Rest = new FoundryRestClient(Config, Credential, _httpClient);
    }

    public string LabName { get; }

    public string[] Args { get; }

    public WorkshopConfig Config { get; }

    public TokenCredential Credential { get; }

    public FoundryRestClient Rest { get; }

    public bool HasFlag(string flag) =>
        Args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));

    public AIProjectClient CreateProjectClient() => new(Config.ProjectUri, Credential);

    public void Dispose()
    {
        Rest.Dispose();
        _httpClient.Dispose();
    }
}
