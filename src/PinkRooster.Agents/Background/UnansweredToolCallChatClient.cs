using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Background;

/// <summary>
/// Gives every tool call that has no result one that says it was cancelled, in each request. An agent with an inbox saves its
/// history after every model call, so a run that is cancelled, or a process that dies, while a tool runs leaves the call in the
/// history without its result, and a provider rejects a request that holds such a call.
/// </summary>
/// <remarks>
/// It sits below MAF's history persistence, where a request is the whole history and the new input. The stored history is not
/// rewritten: the same answer is added to every later request, so a session restored from a file is healed too.
/// </remarks>
internal sealed class UnansweredToolCallChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public const string CancelledResult =
        "Cancelled: the run was stopped before this tool call returned, so its result is unknown. " +
        "Check what it did before you rely on it or call it again.";

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Answered(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Answered(messages), options, cancellationToken);

    private static IEnumerable<ChatMessage> Answered(IEnumerable<ChatMessage> messages)
    {
        List<ChatMessage> request = [.. messages];
        HashSet<string> answered = [.. request.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.CallId)];
        if (request.SelectMany(message => message.Contents).OfType<FunctionCallContent>().All(call => answered.Contains(call.CallId)))
        {
            return request;
        }

        List<ChatMessage> complete = [];
        foreach (ChatMessage message in request)
        {
            complete.Add(message);
            AIContent[] missing = [.. message.Contents.OfType<FunctionCallContent>()
                .Where(call => answered.Add(call.CallId))
                .Select(call => (AIContent)new FunctionResultContent(call.CallId, CancelledResult))];
            if (missing.Length > 0)
            {
                // Directly after the message that made the calls, where a provider looks for their results.
                complete.Add(new ChatMessage(ChatRole.Tool, missing));
            }
        }
        return complete;
    }
}
