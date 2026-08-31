using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace FoundryWorkshop.Shared;

public sealed class FoundryChatClient(
    FoundryRestClient restClient,
    string model) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var input = messages.Select(message => new
        {
            role = message.Role.Value,
            content = message.Text
        });

        using var response = await restClient.CreateResponseAsync(
            new { model, input },
            cancellationToken);
        var text = JsonHelpers.GetOutputText(response.RootElement);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var input = messages.Select(message => new
        {
            role = message.Role.Value,
            content = message.Text
        });

        await foreach (var delta in restClient.StreamResponseTextAsync(
                           new { model, input, stream = true },
                           cancellationToken))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, delta);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        if (serviceKey is null && serviceType == typeof(ChatClientMetadata))
        {
            return new ChatClientMetadata("Microsoft Foundry", new Uri("https://ai.azure.com"), model);
        }

        return null;
    }

    public void Dispose()
    {
    }
}
