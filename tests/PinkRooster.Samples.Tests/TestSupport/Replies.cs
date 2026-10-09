using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.Tests.TestSupport;

/// <summary>Scripted model replies.</summary>
internal static class Replies
{
    public static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    public static ChatMessage Call(string id, string name, Dictionary<string, object?>? arguments = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent(id, name, arguments ?? [])]);

    public static ChatMessage AddTodos(string id, params string[] titles) =>
        Call(id, "todos_add", new() { ["todos"] = titles.Select(title => new Dictionary<string, object?> { ["title"] = title }).ToArray() });

    public static ChatMessage CompleteTodos(string id, params int[] ids) =>
        Call(id, "todos_complete", new() { ["items"] = ids.Select(item => new Dictionary<string, object?> { ["id"] = item, ["reason"] = "done" }).ToArray() });

    /// <summary>Everything a request told the model: its system instructions and every message's text.</summary>
    public static string Prompt(Agents.Tests.TestSupport.ScriptedChatClient model, int request) =>
        string.Join("\n", [model.Options[request]?.Instructions ?? string.Empty, .. model.Requests[request].Select(message => message.Text)]);
}
