namespace PinkRooster.ToolCollections;

/// <summary>
/// What a tool does, declared on the tool with <see cref="ToolAttribute.Kind"/> and read from it with
/// <see cref="ToolKindExtensions.GetKind"/>. A host uses it to decide which calls to allow without asking.
/// </summary>
public enum ToolKind
{
    /// <summary>The tool declares no kind, so nothing is known about what it does.</summary>
    None,

    /// <summary>The tool changes nothing: it reads files, searches, or reports state.</summary>
    Read,

    /// <summary>The tool changes only the agent's own state, such as its task list, or starts work whose own calls are answered separately, such as a sub-agent.</summary>
    State,

    /// <summary>The tool changes files or other data that outlive the run.</summary>
    Edit,

    /// <summary>The tool runs a command or code, which can do anything the process may.</summary>
    Execute
}
