using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using PinkRooster.OpenAI.Reasoning;

namespace PinkRooster.Samples.CodingAgent.ModelSettings;

/// <summary>Builds the <see cref="IChatClient"/> the assistant runs against from one entry of the model settings.</summary>
public static class ModelClient
{
    /// <summary>
    /// A client for the model, with the configured reasoning effort on every request unless the agent sets its own, and with
    /// <see cref="ReasoningFieldChatClient"/> around it, so the reasoning text a compatible server sends in its own field is shown.
    /// </summary>
    public static IChatClient Create(ModelEntry entry)
    {
        // A local server such as Ollama ignores the key, but the client needs one.
        OpenAIClient openAiClient = new(new ApiKeyCredential(entry.ApiKey ?? "unused"), new OpenAIClientOptions { Endpoint = entry.Endpoint });
#pragma warning disable OPENAI001 // The Responses client is still marked experimental by the OpenAI SDK itself.
        IChatClient inner = entry.Api switch
        {
            ModelApi.Responses => openAiClient.GetResponsesClient().AsIChatClient(entry.Model),
            _ => openAiClient.GetChatClient(entry.Model).AsIChatClient()
        };
#pragma warning restore OPENAI001

        return new ChatClientBuilder(inner)
            .Use(client => new ReasoningFieldChatClient(client))
            .ConfigureOptions(options =>
            {
                options.Reasoning ??= new ReasoningOptions();
                options.Reasoning.Effort ??= entry.ReasoningEffort;
            })
            .Build();
    }
}
