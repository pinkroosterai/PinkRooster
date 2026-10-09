using Microsoft.Extensions.AI;
using PinkRooster.Samples.Models;
using PinkRooster.Samples.Terminal;
using Spectre.Console;

namespace PinkRooster.Samples;

/// <summary>
/// Everything a sample does around its feature: reads <c>models.json</c>, picks a model, builds the client and the screen, handles
/// Ctrl+C and failures, and turns the result into an exit code. A sample's own code is its <c>RunAsync</c>.
/// </summary>
public static class SampleHost
{
    /// <summary>Exit code for a run cancelled with Ctrl+C, the shell convention.</summary>
    public const int Cancelled = 130;

    /// <summary>Runs a sample against the model in <c>models.json</c>, on the process's console.</summary>
    /// <param name="run">The sample's <c>RunAsync</c>.</param>
    /// <param name="needsTerminal">True when the sample asks the user something, so it cannot run with redirected input.</param>
    /// <param name="usesTools">True when the sample only shows its feature if the model calls a tool; the host says so when it called none.</param>
    /// <returns>0 when the sample ran, 1 when its setup or its run failed, 130 when cancelled.</returns>
    public static async Task<int> RunAsync(Func<IChatClient, ISampleConsole, CancellationToken, Task> run, bool needsTerminal = false, bool usesTools = false)
    {
        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        return await RunAsync(ModelsFile.DefaultPath, AnsiConsole.Console, ModelClient.Create, run, needsTerminal, shutdown.Token, usesTools);
    }

    /// <summary>The same, with the models file, the screen, the client factory and the cancellation token given; for tests.</summary>
    public static async Task<int> RunAsync(
        string modelsPath,
        IAnsiConsole ansiConsole,
        Func<ModelSettings, IChatClient> createClient,
        Func<IChatClient, ISampleConsole, CancellationToken, Task> run,
        bool needsTerminal,
        CancellationToken cancellationToken,
        bool usesTools = false)
    {
        IReadOnlyList<ModelSettings> models;
        try
        {
            models = ModelsFile.Load(modelsPath, out bool created);
            if (created)
            {
                ansiConsole.MarkupLineInterpolated($"Created {modelsPath} with Ollama's {ModelSettings.DefaultModel}. Edit it to use another model; models.example.json shows how.");
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ansiConsole.WriteLine(error.Message);
            return 1;
        }

        // Every configured key is redacted, not only the chosen one's.
        SpectreSampleConsole console = new(ansiConsole, models.Select(model => model.ApiKey).OfType<string>());
        if (needsTerminal && !console.IsInteractive)
        {
            ansiConsole.WriteLine("This sample asks you questions, so it needs an interactive terminal.");
            return 1;
        }

        try
        {
            ModelSettings settings = await console.SelectModelAsync(models, cancellationToken);
            console.WriteModel(settings);
            ToolCallWatchingChatClient watched = new(createClient(settings));
            await run(watched, console, cancellationToken);
            if (usesTools && watched.ToolCalls == 0)
            {
                // The model, not the sample, is the likely cause: a model without tool support answers in text and never calls one.
                console.WriteLine($"The model called no tool, so this sample did not show its feature. Use a model that supports tool calls: {settings.Model} may not.");
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled;
        }
        catch (Exception error)
        {
            console.WriteError(error);
            return 1;
        }
    }
}
