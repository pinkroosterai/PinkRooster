using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace PinkRooster.Samples.Models;

/// <summary>Builds the <see cref="IChatClient"/> a sample runs against from one entry of <c>models.json</c>.</summary>
public static class ModelClient
{
    /// <summary>
    /// A client for the model, with the configured reasoning effort on every request unless the agent sets its own. It adds no
    /// reasoning wrapper: <c>ReasoningFieldChatClient</c> is the feature of the <c>Reasoning</c> sample.
    /// </summary>
    public static IChatClient Create(ModelSettings settings)
    {
        // A local server such as Ollama ignores the key, but the client needs one.
        OpenAIClient openAiClient = new(new ApiKeyCredential(settings.ApiKey ?? "unused"), new OpenAIClientOptions { Endpoint = settings.Endpoint });
#pragma warning disable OPENAI001 // The Responses client is still marked experimental by the OpenAI SDK itself.
        IChatClient inner = settings.Api switch
        {
            ModelApi.Responses => openAiClient.GetResponsesClient().AsIChatClient(settings.Model),
            _ => openAiClient.GetChatClient(settings.Model).AsIChatClient()
        };
#pragma warning restore OPENAI001

        return new ChatClientBuilder(inner)
            .ConfigureOptions(options =>
            {
                options.Reasoning ??= new ReasoningOptions();
                options.Reasoning.Effort ??= settings.ReasoningEffort;
            })
            .Build();
    }
}
