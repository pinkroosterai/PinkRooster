// Agent composition template only.
// Remove sections and hooks that the agent does not actually need.
// Do not invent business logic, tools, providers, or middleware to fill the template.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Agents;
using PinkRooster.ToolCollections;

namespace Example;

public static class ExampleAgent
{
    // Returning AgentBuilder keeps application-specific callers free to add
    // metadata, events, logging, or other local composition before Build().
    public static AgentBuilder Create(
        IChatClient chatClient,
        ToolCollection tools,
        AIContextProvider? contextProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(tools);

        AgentBuilder builder = chatClient.CreateAgent()
            .WithName("ExampleAgent")
            .WithDescription("Handles the Example domain capability.")
            .WithRole("You are the Example-domain assistant.")
            .WithObjective("Complete the user's Example-domain request or state precisely what prevents completion.")
            .WithBackground("Add only stable domain context the agent needs on nearly every request.")
            .WithInstruction("Add only non-obvious operational behavior that spans this agent.")
            .WithConstraint("Add only a real behavioral boundary; enforce security-critical policy outside the prompt.")
            .WithTools(tools);

        // Prefer structured output at the MAF invocation/options layer when the
        // application needs machine-readable data; do not add "return JSON" here
        // by default.

        // Add proactive dynamic context only when it belongs on every run.
        if (contextProvider is not null)
        {
            builder.WithContextProvider(contextProvider);
        }

        if (loggerFactory is not null)
        {
            builder.WithLoggerFactory(loggerFactory);
        }

        // Agent-specific function approval, if needed:
        // builder.RequireApproval("ConsequentialTool");

        // Use ConfigureChatOptions / ConfigureAgentOptions / ConfigureClient /
        // ConfigureToolLoop only for settings not covered by first-class builder
        // methods and only when the agent has a concrete requirement for them.

        return builder;
    }
}

// Typical caller:
// AIAgent agent = ExampleAgent.Create(chatClient, tools).Build();
