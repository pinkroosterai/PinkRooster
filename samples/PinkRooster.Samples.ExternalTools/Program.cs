using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Samples.Terminal;
using PinkRooster.ToolCollections;

namespace PinkRooster.Samples.ExternalTools;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        // Functions that did not start as a ToolCollection class: generated ones, or another SDK's.
        AIFunction weather = AIFunctionFactory.Create(GetWeather, "GetWeather", "Returns today's weather for a city.");
        AIFunction restart = AIFunctionFactory.Create(() => "The sensor restarted.", "RestartSensor", "Restarts the weather sensor.");

        ExternalToolCollection sensors = new ExternalToolCollection("Weather", [weather, restart])
            .WithInstruction("Temperatures are in Celsius.")
            .RequireApproval("RestartSensor")
            .WithContext(_ => ValueTask.FromResult<string?>("## Sensors\nOnline: 3"));

        AIAgent agent = chatClient.CreateAgent().WithRole("You answer questions about the weather.").WithTools(sensors).Build();
        AgentResponse response = await agent.RunAsync("What is the weather in Utrecht?", cancellationToken: cancellationToken);

        foreach (ToolApprovalRequestContent request in response.GetApprovalRequests())
        {
            console.WriteLine($"{(request.ToolCall as FunctionCallContent)?.Name} needs approval; the Approvals sample shows how to answer.");
        }

        console.WriteAnswer(response.Text);
    }

    private static string GetWeather([Description("The city.")] string city) => $"{city}: 14 degrees, light rain.";
}
