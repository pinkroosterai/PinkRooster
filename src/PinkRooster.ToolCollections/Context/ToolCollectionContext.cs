using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PinkRooster.ToolCollections.Context;

/// <summary>Collects the current context of several collections into one text, and their standing text into prompt sections.</summary>
internal static class ToolCollectionContext
{
    /// <summary>The first line of the context text, so the model does not take it for something the user wrote.</summary>
    public const string FramingLine = "The current state of your tools, added automatically before this model call. The user did not write this.";

    /// <summary>
    /// Returns the framing line and every collection's non-blank context, in the order given, or null when none has any.
    /// A collection that throws is logged, to a null logger when there is none, and left out; the run's own cancellation passes through.
    /// </summary>
    public static async ValueTask<string?> BuildAsync(IReadOnlyList<ToolCollection> collections, ILogger? logger, CancellationToken cancellationToken)
    {
        logger ??= NullLogger.Instance;
        List<string> blocks = [];
        foreach (ToolCollection collection in collections)
        {
            string? text;
            try
            {
                text = await collection.GetContextAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Tool collection context failed; the collection is left out of this model call. Collection: {Collection}", collection.DisplayName);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                blocks.Add(text);
            }
        }

        return blocks.Count == 0 ? null : $"{FramingLine}\n\n{string.Join("\n\n", blocks)}";
    }

    /// <summary>Throws when two collections expose a tool with the same name (ignoring case), naming both collections.</summary>
    public static void ThrowOnDuplicateToolNames(IReadOnlyList<ToolCollection> collections)
    {
        Dictionary<string, ToolCollection> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (ToolCollection collection in collections)
        {
            foreach (string name in collection.GetAIFunctions().Select(function => function.Name))
            {
                if (owners.TryGetValue(name, out ToolCollection? owner))
                {
                    throw new InvalidOperationException(
                        $"Duplicate tool name '{name}' in '{owner.DisplayName}' and '{collection.DisplayName}'. Tool names must be unique; " +
                        "rename one with [Tool(\"<name>\", ...)], or give an added tool a different name before adding it.");
                }
                owners.Add(name, collection);
            }
        }
    }

    /// <summary>Copies the collections, rejecting a null list or a null entry.</summary>
    public static ToolCollection[] Copy(IEnumerable<ToolCollection> collections, string paramName)
    {
        ArgumentNullException.ThrowIfNull(collections, paramName);
        ToolCollection[] copy = [.. collections];
        if (copy.Any(collection => collection is null))
        {
            throw new ArgumentException("Every tool collection must be non-null.", paramName);
        }
        return copy;
    }
}
