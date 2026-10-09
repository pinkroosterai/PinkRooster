using System.Text;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>Writes a task list as text: a short view for the model's context, and a full one for the <c>TaskGet</c> tool.</summary>
internal static class TaskListFormatter
{
    /// <summary>
    /// The compact view injected before each model call: the counts, then open tasks with in-progress first, then startable, then
    /// blocked, cut at <see cref="TaskListOptions.MaxVisibleItems"/>; completed tasks are only counted. Null for an empty list.
    /// </summary>
    public static string? FormatContext(TaskListSnapshot snapshot, TaskListOptions options)
    {
        if (snapshot.Items.Count == 0)
        {
            return null;
        }

        int completed = snapshot.Items.Count(item => item.Status == TaskItemStatus.Completed);
        List<TaskItem> open = [.. snapshot.Items
            .Where(item => item.Status != TaskItemStatus.Completed)
            .OrderBy(item => item.Status == TaskItemStatus.InProgress ? 0 : OpenBlockers(snapshot, item).Count == 0 ? 1 : 2)
            .ThenBy(item => item.Id)];

        StringBuilder text = new();
        text.Append("## Task list (").Append(completed).Append('/').Append(snapshot.Items.Count).Append(" done)");
        foreach (TaskItem item in open.Take(options.MaxVisibleItems))
        {
            text.Append('\n').Append(item.Status == TaskItemStatus.InProgress ? "> " : string.Empty)
                .Append('#').Append(item.Id).Append(" [").Append(StatusName(item.Status)).Append("] ")
                .Append(Truncate(item.Subject, options.MaxSubjectLength));
            List<int> blockers = OpenBlockers(snapshot, item);
            if (blockers.Count > 0)
            {
                text.Append(" (blocked by ").Append(Ids(blockers)).Append(')');
            }
        }

        if (open.Count > options.MaxVisibleItems)
        {
            text.Append("\n+").Append(open.Count - options.MaxVisibleItems).Append(" more open (TaskGet lists all).");
        }

        if (open.Count == 0)
        {
            text.Append("\nAll tasks completed.");
        }
        else if (completed > 0)
        {
            text.Append("\nCompleted: ").Append(completed).Append('.');
        }

        return text.ToString();
    }

    /// <summary>The full detail of the given tasks, or of every task when <paramref name="ids"/> is empty; ids with no task are named.</summary>
    public static string FormatDetail(TaskListSnapshot snapshot, IReadOnlyList<int> ids)
    {
        if (snapshot.Items.Count == 0)
        {
            return "No tasks.";
        }

        bool all = ids.Count == 0;
        StringBuilder text = new();
        if (all)
        {
            text.Append("Tasks (").Append(snapshot.Items.Count(item => item.Status == TaskItemStatus.Completed))
                .Append('/').Append(snapshot.Items.Count).Append(" done):");
        }

        IEnumerable<int> wanted = all ? snapshot.Items.Select(item => item.Id) : ids.Distinct();
        foreach (int id in wanted)
        {
            TaskItem? item = snapshot.Items.Find(candidate => candidate.Id == id);
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            if (item is null)
            {
                text.Append('#').Append(id).Append(" not found.");
                continue;
            }

            text.Append('#').Append(item.Id).Append(" [").Append(StatusName(item.Status)).Append("] ").Append(item.Subject);
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                text.Append("\n  ").Append(item.Description);
            }

            if (item.BlockedBy.Count > 0)
            {
                List<int> open = OpenBlockers(snapshot, item);
                text.Append("\n  Blocked by: ").Append(Ids(item.BlockedBy));
                if (open.Count > 0 && open.Count != item.BlockedBy.Count)
                {
                    text.Append(" (open: ").Append(Ids(open)).Append(')');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>The tasks that block <paramref name="item"/> and are not completed yet.</summary>
    public static List<int> OpenBlockers(TaskListSnapshot snapshot, TaskItem item) =>
        [.. item.BlockedBy.Where(id => snapshot.Items.Any(other => other.Id == id && other.Status != TaskItemStatus.Completed))];

    public static string StatusName(TaskItemStatus status) => status switch
    {
        TaskItemStatus.InProgress => "in_progress",
        TaskItemStatus.Completed => "completed",
        _ => "pending"
    };

    public static string Ids(IEnumerable<int> ids) => string.Join(",", ids.Select(id => "#" + id));

    private static string Truncate(string subject, int maxLength)
    {
        string oneLine = string.Join(' ', subject.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= maxLength ? oneLine : oneLine[..(maxLength - 3)] + "...";
    }
}
