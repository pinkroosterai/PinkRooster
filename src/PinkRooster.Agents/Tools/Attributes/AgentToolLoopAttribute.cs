// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Sets limits of the tool loop of a <see cref="DeclaredAgent"/> class, the way the builder's <c>ConfigureToolLoop</c> does.</summary>
/// <remarks>
/// <para>
/// Only the properties that are set change the loop; the others keep Microsoft.Extensions.AI's defaults. A class holds one. On a class hierarchy the base
/// class's settings apply first and the derived class's replace them property by property. A <c>ConfigureToolLoop</c> call in <c>Configure</c> runs after these and wins.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AgentToolLoop(MaximumIterationsPerRequest = 10, IncludeDetailedErrors = true)]
/// public sealed class BuildAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AgentToolLoopAttribute : Attribute
{
    private int? maximumIterationsPerRequest;
    private int? maximumConsecutiveErrorsPerRequest;
    private bool? includeDetailedErrors;
    private bool? allowConcurrentInvocation;
    private bool? terminateOnUnknownCalls;

    /// <summary>The most model calls one request may make; see <c>FunctionInvokingChatClient.MaximumIterationsPerRequest</c>.</summary>
    public int MaximumIterationsPerRequest
    {
        get => maximumIterationsPerRequest ?? 0;
        set => maximumIterationsPerRequest = value;
    }

    /// <summary>The most consecutive failing tool calls one request may make; see <c>FunctionInvokingChatClient.MaximumConsecutiveErrorsPerRequest</c>.</summary>
    public int MaximumConsecutiveErrorsPerRequest
    {
        get => maximumConsecutiveErrorsPerRequest ?? 0;
        set => maximumConsecutiveErrorsPerRequest = value;
    }

    /// <summary>Whether the model is told the detail of a tool's exception; see <c>FunctionInvokingChatClient.IncludeDetailedErrors</c>.</summary>
    public bool IncludeDetailedErrors
    {
        get => includeDetailedErrors ?? false;
        set => includeDetailedErrors = value;
    }

    /// <summary>Whether tool calls of one model reply may run at the same time; see <c>FunctionInvokingChatClient.AllowConcurrentInvocation</c>.</summary>
    public bool AllowConcurrentInvocation
    {
        get => allowConcurrentInvocation ?? false;
        set => allowConcurrentInvocation = value;
    }

    /// <summary>Whether a call to an unknown tool ends the loop; see <c>FunctionInvokingChatClient.TerminateOnUnknownCalls</c>.</summary>
    public bool TerminateOnUnknownCalls
    {
        get => terminateOnUnknownCalls ?? false;
        set => terminateOnUnknownCalls = value;
    }

    internal void ApplyTo(Microsoft.Extensions.AI.FunctionInvokingChatClient loop)
    {
        if (maximumIterationsPerRequest is int iterations)
        {
            loop.MaximumIterationsPerRequest = iterations;
        }
        if (maximumConsecutiveErrorsPerRequest is int errors)
        {
            loop.MaximumConsecutiveErrorsPerRequest = errors;
        }
        if (includeDetailedErrors is bool detailed)
        {
            loop.IncludeDetailedErrors = detailed;
        }
        if (allowConcurrentInvocation is bool concurrent)
        {
            loop.AllowConcurrentInvocation = concurrent;
        }
        if (terminateOnUnknownCalls is bool terminate)
        {
            loop.TerminateOnUnknownCalls = terminate;
        }
    }
}
