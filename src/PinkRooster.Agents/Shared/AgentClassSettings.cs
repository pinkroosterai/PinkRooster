
namespace PinkRooster.Agents.Shared;

/// <summary>Applies the attributes of an agent class that set one builder option each: description, defaults, tool loop, approvals and background tools.</summary>
internal static class AgentClassSettings
{
    /// <summary>Puts what <paramref name="type"/> and its base types declare on <paramref name="builder"/>, base types first.</summary>
    /// <exception cref="InvalidOperationException">An attribute holds blank text; the message names the class, the attribute and the fix.</exception>
    public static void Apply(Type type, AgentBuilder builder)
    {
        List<Type> chain = TypeChain.BaseFirst(type);

        List<AgentToolLoopAttribute> loops = [];
        foreach (Type current in chain)
        {
            if (current.GetCustomAttributes(typeof(AgentDescriptionAttribute), inherit: false).Cast<AgentDescriptionAttribute>().FirstOrDefault() is AgentDescriptionAttribute description)
            {
                if (string.IsNullOrWhiteSpace(description.Description))
                {
                    throw TypeChain.Blank(typeof(AgentDescriptionAttribute), type, current);
                }
                builder.WithDescription(description.Description);
            }
            if (current.IsDefined(typeof(AgentWithoutDefaultsAttribute), inherit: false))
            {
                builder.WithoutDefaults();
            }
            if (current.GetCustomAttributes(typeof(AgentToolLoopAttribute), inherit: false).Cast<AgentToolLoopAttribute>().FirstOrDefault() is AgentToolLoopAttribute loop)
            {
                loops.Add(loop);
            }
            foreach (AgentRequireApprovalAttribute approval in current.GetCustomAttributes(typeof(AgentRequireApprovalAttribute), inherit: false).Cast<AgentRequireApprovalAttribute>())
            {
                if (approval.ToolNames.Length == 0 || approval.ToolNames.Any(string.IsNullOrWhiteSpace))
                {
                    throw TypeChain.Blank(typeof(AgentRequireApprovalAttribute), type, current);
                }
                builder.RequireApproval(approval.ToolNames);
            }
            foreach (AgentAllowBackgroundAttribute background in current.GetCustomAttributes(typeof(AgentAllowBackgroundAttribute), inherit: false).Cast<AgentAllowBackgroundAttribute>())
            {
                if (background.ToolNames.Length == 0 || background.ToolNames.Any(string.IsNullOrWhiteSpace))
                {
                    throw TypeChain.Blank(typeof(AgentAllowBackgroundAttribute), type, current);
                }
                builder.AllowBackground(background.ToolNames);
            }
        }

        if (loops.Count > 0)
        {
            builder.ConfigureToolLoop(client =>
            {
                foreach (AgentToolLoopAttribute loop in loops)
                {
                    loop.ApplyTo(client);
                }
            });
        }
    }
}
