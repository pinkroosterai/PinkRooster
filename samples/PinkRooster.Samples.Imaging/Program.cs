using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Samples.Terminal;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Images;

namespace PinkRooster.Samples.Imaging;

public static class Program
{
    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, usesTools: true);

    /// <summary>The sample on one model: it runs the agent and looks at the images. It has to accept images.</summary>
    public static Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken) =>
        RunAsync(chatClient, visionClient: chatClient, console, cancellationToken);

    /// <summary>The same with a model of its own for the images, so the agent's model does not have to see; the tests give two scripted ones.</summary>
    public static async Task RunAsync(IChatClient chatClient, IChatClient visionClient, ISampleConsole console, CancellationToken cancellationToken)
    {
        // The two images ship with the sample. Their folder is the workspace: the tool reads nothing outside it.
        Workspace workspace = new(Path.Combine(AppContext.BaseDirectory, "images"));

        // The vision client is the host's own, so the host can wrap it: here to count what looking at images costs.
        VisionUsage vision = new(visionClient);

        ImageToolCollection images = new ImageToolCollectionBuilder()
            .InWorkspace(workspace)
            .WithVisionClient(vision)
            // A call with more images than this is refused with an error the model reads.
            .WithMaxImagesPerCall(2)
            // A vision call that takes longer is stopped, and the model is told so.
            .WithTimeout(TimeSpan.FromSeconds(90))
            .Build();

        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You answer questions about the image files in your base directory.")
            .WithTools(images)
            .OnEvent<ToolCallStarted>(call => console.WriteLine($"-> {call.Name}"))
            .Build();

        AgentResponse response = await agent.RunAsync(
            "build-error.png is a screenshot of a failed build. Quote the line with the error. " +
            "Then say what sales-chart.png shows and which quarter is the highest.",
            cancellationToken: cancellationToken);

        console.WriteAnswer(response.Text);
        // The images went to the vision model and never into the agent's own conversation: what came back is text.
        console.WriteLine($"Vision calls: {vision.Calls}, with {vision.InputTokens} input and {vision.OutputTokens} output tokens.");
    }

    /// <summary>Counts the requests to the vision model and the tokens they used. Their usage is not part of the agent's own.</summary>
    private sealed class VisionUsage(IChatClient inner) : DelegatingChatClient(inner)
    {
        private int calls;
        private long inputTokens;
        private long outputTokens;

        public int Calls => Volatile.Read(ref calls);

        public long InputTokens => Interlocked.Read(ref inputTokens);

        public long OutputTokens => Interlocked.Read(ref outputTokens);

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken);
            Interlocked.Add(ref inputTokens, response.Usage?.InputTokenCount ?? 0);
            Interlocked.Add(ref outputTokens, response.Usage?.OutputTokenCount ?? 0);
            return response;
        }
    }
}
