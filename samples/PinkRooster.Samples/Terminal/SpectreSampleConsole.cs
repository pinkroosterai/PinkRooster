using System.ClientModel;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.Samples.Models;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;
using Spectre.Console;

namespace PinkRooster.Samples.Terminal;

/// <summary>A sample's screen: an <see cref="AgentConsole"/> from <c>PinkRooster.SpectreConsole</c> for the run, approvals and questions, and the sample-only error text and model selection.</summary>
public sealed class SpectreSampleConsole : ISampleConsole
{
    private const string Accent = "#ff5faf";
    private const string Muted = "#8a8a8a";

    private readonly IAnsiConsole console;
    private readonly AgentConsole agent;

    public SpectreSampleConsole(IAnsiConsole console, IEnumerable<string>? sensitiveValues = null)
    {
        this.console = console;
        agent = new AgentConsole(console, new AgentConsoleOptions { SensitiveValues = [.. sensitiveValues ?? []] });
    }

    public bool IsInteractive => agent.IsInteractive;

    /// <summary>Writes what the sample talks to, as its first line.</summary>
    public void WriteModel(ModelSettings settings) =>
        console.MarkupLine($"[bold]PinkRooster sample[/] [{Muted}]with {Markup.Escape(settings.Model)} on {Markup.Escape(settings.Endpoint.Host)}[/]");

    /// <summary>Lets the user pick the model; the only one, or the first on a terminal that cannot ask, needs no pick.</summary>
    public async Task<ModelSettings> SelectModelAsync(IReadOnlyList<ModelSettings> models, CancellationToken cancellationToken)
    {
        if (models.Count == 1 || !IsInteractive)
        {
            return models[0];
        }

        return await new SelectionPrompt<ModelSettings>()
            .Title("[bold]Which model?[/]")
            .AddChoices(models)
            .UseConverter(model => $"{Markup.Escape(model.Model)} [{Muted}]on {Markup.Escape(model.Endpoint.Host)}[/]")
            .HighlightStyle(new Style(Color.FromHex(Accent), decoration: Decoration.Bold))
            .ShowAsync(console, cancellationToken);
    }

    public void WriteLine(string text = "") => agent.WriteLine(text);

    public void WriteAnswer(string text) => agent.WriteAnswer(text);

    public void Write(string text) => agent.Write(text);

    public void WriteEvent(AgentEvent item) => agent.WriteEvent(item);

    public Task<ApprovalChoice> ConfirmToolCallAsync(ApprovalQuestion question, CancellationToken cancellationToken) =>
        agent.ConfirmToolCallAsync(question, cancellationToken);

    public Task<IReadOnlyList<UserQuestionAnswer>> AskAsync(IReadOnlyList<UserQuestion> questions, CancellationToken cancellationToken) =>
        agent.AskAsync(questions, cancellationToken);

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken) =>
        agent.ReadLineAsync(prompt, cancellationToken);

    // Written as plain text through the agent console, so an open line is ended first and its text is redacted.
    public void WriteError(Exception error)
    {
        if (error is ClientResultException serviceError && serviceError.Status > 0)
        {
            string guidance = serviceError.Status switch
            {
                401 or 403 => "Check this model's apiKey in models.json and that the key can use this model.",
                404 => "Check this model's endpoint and model ID in models.json; for Ollama, pull the model first with ollama pull.",
                429 => "The service's rate limit or quota was reached; wait and try again.",
                _ => "Check the model, endpoint, and that the model supports tool calls."
            };
            agent.WriteLine($"Request failed with HTTP {serviceError.Status}. {guidance}");
        }
        else if (error is HttpRequestException)
        {
            agent.WriteLine("Could not reach the model server. Check that it is running, for Ollama with ollama serve, and check this model's endpoint in models.json.");
        }
        else
        {
            agent.WriteLine($"The sample failed ({error.GetType().Name}).");
        }

        agent.WriteLine(error.ToString());
    }
}
