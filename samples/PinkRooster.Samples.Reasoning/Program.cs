using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.OpenAI.Reasoning;
using PinkRooster.Samples.Terminal;

namespace PinkRooster.Samples.Reasoning;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        // Ollama and Groq send reasoning in a "reasoning" field that the OpenAI adapter drops; this client adds it back.
        IChatClient withReasoning = new ReasoningFieldChatClient(chatClient);

        AIAgent agent = withReasoning
            .CreateAgent()
            .WithRole("You solve small puzzles.")
            // Ask for the reasoning in full; the effort is the one models.json sets for this model.
            .ConfigureChatOptions(options => options.Reasoning = new ReasoningOptions { Output = ReasoningOutput.Full })
            .OnEvent(item =>
            {
                switch (item)
                {
                    case ReasoningCompleted thought: console.WriteLine($"Reasoning: {thought.Text}"); break;
                    case AssistantTextCompleted answer: console.WriteLine($"Answer: {answer.Text}"); break;
                }
            })
            .Build();

        await agent.RunAsync("A bat and a ball cost 1.10 together; the bat costs 1.00 more than the ball. What does the ball cost?", cancellationToken: cancellationToken);
    }
}
