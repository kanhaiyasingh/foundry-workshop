using Azure.Core;
using Azure.Identity;
using DotNetEnv;

namespace FoundryWorkshop.Shared;

public sealed class WorkshopConfig
{
    private WorkshopConfig()
    {
    }

    public static WorkshopConfig Load()
    {
        Env.NoClobber().TraversePath().Load();
        return new WorkshopConfig();
    }

    public string? this[string name] => Environment.GetEnvironmentVariable(name);

    public string ProjectEndpoint => Require("PROJECT_ENDPOINT");

    public Uri ProjectUri => RequireUri("PROJECT_ENDPOINT");

    public Uri AccountUri
    {
        get
        {
            var marker = "/api/projects/";
            var endpoint = ProjectEndpoint;
            var index = endpoint.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return new Uri((index >= 0 ? endpoint[..index] : endpoint).TrimEnd('/') + "/");
        }
    }

    public string ChatModel => Get("CHAT_MODEL", "gpt-4.1-mini");

    public string EmbeddingModel => Get("EMBEDDING_MODEL", "text-embedding-3-large");

    public string Get(string name, string fallback) =>
        string.IsNullOrWhiteSpace(this[name]) ? fallback : this[name]!;

    public string Require(string name, string? guidance = null)
    {
        var value = this[name];
        if (!string.IsNullOrWhiteSpace(value) && !value.Contains('<', StringComparison.Ordinal))
        {
            return value;
        }

        var suffix = string.IsNullOrWhiteSpace(guidance) ? string.Empty : $" {guidance}";
        throw new WorkshopConfigurationException(
            $"Set {name} in the repository .env file.{suffix}");
    }

    public Uri RequireUri(string name, string? guidance = null)
    {
        var value = Require(name, guidance);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new WorkshopConfigurationException($"{name} must be an absolute URI.");
        }

        return uri;
    }

    public bool IsConfigured(string name)
    {
        var value = this[name];
        return !string.IsNullOrWhiteSpace(value) && !value.Contains('<', StringComparison.Ordinal);
    }

    public TokenCredential CreateCredential()
    {
        var tenantId = this["AZURE_TENANT_ID"];
        if (Get("AZURE_AUTH_MODE", "default").Equals("cli", StringComparison.OrdinalIgnoreCase))
        {
            return new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId });
        }

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId = tenantId,
            ExcludeInteractiveBrowserCredential = true
        });
    }
}

public sealed class WorkshopConfigurationException(string message) : Exception(message);
