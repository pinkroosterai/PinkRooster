using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Steps;
using PinkRooster.Samples.Terminal;

namespace PinkRooster.Samples.SteppedAgents;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        TranslatorAgent translator = new(chatClient);
        using IDisposable steps = translator.OnEvent<StepStarted>(step => console.WriteLine($"step: {step.Label}"));

        AgentResponse response = await translator.RunAsync("Hallo wereld, dit is een test van de vertaler.", cancellationToken: cancellationToken);

        // The answer is the last turn's reply.
        console.WriteAnswer(response.Text);
    }
}

/// <summary>A stepped agent class: translates, then reads its own translation once more against the original.</summary>
[AgentRole("You translate documents into plain English.")]
[AgentOutputFormat("The translation only, with no comment.")]
public sealed class TranslatorAgent(IChatClient chatClient) : SteppedAgent(chatClient)
{
    // What a run remembers lives in the session, never in a field, because one instance serves every session.
    private sealed record Original(string Text);

    // The most turns one message may take, a tool approval in between included.
    protected override int MaxTurns => 2;

    protected override ValueTask StartAsync(StepContext step, CancellationToken cancellationToken)
    {
        step.SetState(new Original(step.Request));
        return ValueTask.CompletedTask;
    }

    // Runs after each turn: stop and let the last reply be the answer, or send the next prompt.
    protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken) =>
        ValueTask.FromResult(step.LastReply.Length == 0
            ? NextStep.Stop()
            : NextStep.Send(
                "revise",
                $"Read your translation against the original and fix any mistake. The original:\n{step.GetState<Original>().Text}\nReply with the final translation only.",
                isFinal: true));
}
