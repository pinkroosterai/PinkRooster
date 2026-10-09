using Microsoft.Extensions.AI;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.SubAgents;
using PinkRooster.Samples.CodingAgent.ModelSettings;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Shells;

namespace PinkRooster.Samples.CodingAgent.Helpers;

/// <summary>The helpers the assistant can start with <c>RunSubAgent</c>: one model per entry of the model settings, and two tool sets.</summary>
public static class HelperTools
{
    public const string ReadSet = "read";
    public const string ShellSet = "shell";

    /// <param name="models">Every model of the settings with its client; a helper runs on the one the assistant picks.</param>
    /// <param name="workspace">The workspace the helpers read and run commands in.</param>
    /// <param name="shell">The assistant's own shell collection, which a helper with the shell set shares.</param>
    /// <param name="policy">The policy that answers a helper's approvals, the same one that answers the assistant's.</param>
    public static SubAgentToolCollection Create(IReadOnlyList<(ModelEntry Entry, IChatClient Client)> models, Workspace workspace, ShellToolCollection shell, PermissionPolicy policy)
    {
        // A helper finds, searches and reads; it gets no tool that changes a file.
        FileReadToolCollection read = new(workspace);
        // A helper's command needs approval as the assistant's does. The collection is the same shell with its own standing text,
        // given as functions from elsewhere so the approval sits on the tool whichever set a helper was given.
        ExternalToolCollection approvedShell = new ExternalToolCollection("Shell", shell.GetAIFunctions()).RequireApproval("RunShell");
        foreach (string instruction in shell.Instructions)
        {
            approvedShell.WithInstruction(instruction);
        }
        foreach (string constraint in shell.Constraints)
        {
            approvedShell.WithConstraint(constraint);
        }
        policy.WithTools(read, approvedShell);

        SubAgentToolCollectionBuilder builder = new SubAgentToolCollectionBuilder()
            .WithToolSet(ReadSet, "Finding, searching and reading files. Enough for exploring the code and for a review.", read)
            .WithToolSet(ShellSet, "Running commands, such as a build, a test run or git. Each command is put to the user as yours are.", approvedShell)
            .ApproveToolCallsWith(policy.AnswerAsync);

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach ((ModelEntry entry, IChatClient client) in models)
        {
            // The same model ID on two endpoints is told apart by the endpoint's host.
            string name = names.Add(entry.Model) ? entry.Model : $"{entry.Model}@{entry.Endpoint.Host}";
            names.Add(name);
            builder.WithModel(name, entry.UseWhen, client);
        }
        return builder.Build();
    }
}
