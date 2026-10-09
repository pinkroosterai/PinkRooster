# PinkRooster.ToolCollections.BuiltIn

Ready-made [tool collections](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md) for agents built on the Microsoft Agent Framework: run shell commands, ask the user
multiple-choice questions, keep a task list, tell the time, operate on files within a sandboxed directory, ask a vision model about image files.

```text
dotnet add package PinkRooster.ToolCollections.BuiltIn --prerelease
```

Needs the .NET 10 SDK. It depends on `PinkRooster.ToolCollections`, and on no model provider or UI library. Use it with
[`PinkRooster.Agents`](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md) or any Microsoft Agent Framework (MAF) agent.
The first example below uses the builder, so add `PinkRooster.Agents` too (`dotnet add package PinkRooster.Agents --prerelease`); `chatClient` is any `IChatClient` ([how to make one](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#quickstart)), and `repoRoot` is the folder the agent may work in: a `Workspace` is that folder, shared by the shell and file tools.

```csharp
using PinkRooster.Agents;
using PinkRooster.ToolCollections.BuiltIn.DateTimes;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Shells;

var workspace = new Workspace(repoRoot);
var agent = chatClient
    .CreateAgent()
    .WithRole("You are a build assistant for this repository.")
    .WithTools(
        new ShellToolCollection(workspace),
        new FileOperationsToolCollection(workspace),
        new DateTimeToolCollection())
    .RequireApproval("RunShell")
    .Build();
```

## Builders

A constructor takes what a collection cannot do without: `new ShellToolCollection(workspace)`, `new FileOperationsToolCollection(workspace)`, `new AskUserQuestionToolCollection(console.AskAsync)`. Everything optional is set through the collection's fluent builder, which sits next to it in the collection's own namespace. A builder is created with `new`, every method returns it, and `Build()` can be called more than once. A setting left out keeps its default; a missing required one (`InWorkspace` on the file builders) fails at `Build()` with a message that names the call.

```csharp
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;

var workspace = new Workspace(repoRoot);
var shell = new ShellToolCollectionBuilder()
    .InWorkspace(workspace)
    .WithTimeout(TimeSpan.FromSeconds(30))
    .AllowPrefixes("git", "dotnet")
    .DenyPrefixes("git push")
    .Build();

var files = new FileOperationsToolCollectionBuilder().InWorkspace(workspace).KeepBackups().Build();
var tasks = new TaskListToolCollectionBuilder().WithJsonFile("tasks/session-42.json").WithMaxVisibleItems(10).Build();
```

`FileReadToolCollectionBuilder` and, in `PinkRooster.Agents`, `SubAgentToolCollectionBuilder` work the same way. `AskUserQuestionToolCollection` and `DateTimeToolCollection` have so few settings that their constructors take them all, and they have no builder.

## ShellToolCollection

Adds a `RunShell` tool. Each call runs in a fresh process with closed input, a hard timeout, and output bounded in
memory and in length.

- **Limits.** `WithTimeout` (default 120 s), `WithMaxTimeout` (the longest the model may ask for with `timeoutSeconds`,
  default 10 min), `WithMaxOutputCharacters` (default 30,000; longer output is truncated in the middle). The
  defaults leave room for the errors of a failing build or test run; lower the output limit for a small-context model.
- **Long output.** stdout and stderr are merged into one `Output:` section in the order they arrive, so an error sits next
  to the output it belongs to (`SeparateStreams()` gives `Stdout:` and `Stderr:` sections instead). When the merged output
  does not fit the reply, all of it is also saved to `.pinkrooster/output/shell-<time>-<id>.txt` under the workspace root, and the
  result names the file so the model can read it with `ReadFile` or search it with `SearchFiles`. The folder holds its own
  `.gitignore` (`*`), and files older than a day, or beyond the newest twenty, are removed when a new one is made. With
  `outputFilters` the file holds the lines the filter kept. Separate streams are cut in the middle and not saved. If the file
  cannot be written (or `.pinkrooster` is a link out of the workspace), the result says so and is cut as before. Arrival order
  is the order the two pipes were read, which is the order written for output that is not produced within the same instant.
- **Prefix lists.** `AllowPrefixes` entries match the first words of a command, ignoring case (`git` allows `git log`,
  not `gitk`; `git status` allows only that). With an allowlist, commands containing `; & | ` ( ) < >` or a line break
  are refused as well, so an allowed command cannot carry a second one or write a file by redirection. `DenyPrefixes`
  carves exceptions out of an allowlist (allow `git`, deny `git push`) and needs one: on its own it cannot see past
  chaining, so `Build()` refuses it.
- **Environment.** Commands get the host's environment plus `ShellToolCollection.NonInteractiveEnvironment`, which makes
  tools fail instead of prompting; `WithEnvironmentVariable` overrides or removes variables. `WithoutInheritedEnvironment()` starts from an
  empty environment plus those two, so the host's secrets do not reach commands; then pass `PATH` and what the tools need.
- **Sandbox.** The library names no sandbox. `new Shell(dialect, executable) { Launcher = [...] }` starts the shell through a
  program you choose (`bwrap ... --`, `docker exec -i box`), which is how a host gives commands a real boundary.
- **One call, one directory.** Every call starts in the workspace root, nothing carries over, and a command that only
  changes directory says it had no lasting effect. The model passes `workingDirectory` instead. The collection has no background command of its own; an agent built with `AgentBuilder` can let the model start `RunShell` as a background task with `AllowBackground("RunShell")` (see [background tools](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#background-tools)).
- **Shell.** `Shell.Default` picks Windows PowerShell 5.1 on Windows, otherwise `bash` when it is on `PATH` and `sh` when it
  is not. Models write bash, so `[[ ]]`, arrays and `pipefail` work by default. A result starts with `Status:` and, when
  the command ran, `Exit Code:`; the shell's name is in the collection's instruction and in a `StartFailed` result.
- **Workspace.** `ShellToolCollection(Workspace? workspace = null, ...)` starts commands in the workspace's root and keeps `workingDirectory` inside it. Share the instance with `FileOperationsToolCollection`; its instruction tells the model to use the file tools for reading, searching and changing files when it has them.

**`RunShell` runs unattended unless you ask for approval.** The prefix lists guard against honest mistakes and are not a
sandbox: they match the first words of a command, and commands run with the host's permissions and, unless you turn off
`WithoutInheritedEnvironment()`, its full environment, including any secrets in it. For a boundary, start the shell through a `Launcher`. Call `RequireApproval("RunShell")` on the agent builder to have the host approve each command.
The POSIX path is tested on Linux, the PowerShell paths on Windows.

## FileOperationsToolCollection

Adds fine-grained tools for finding, searching, reading, listing, creating, editing, moving, and deleting files sandboxed to the root of a `Workspace`.

```csharp
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn;

var files = new FileOperationsToolCollection(new Workspace(repoRoot));
```

- **Workspace.** A `Workspace` is the root folder and the folder names the search tools skip (`.git`, `node_modules`, `bin`, `obj` and `.pinkrooster` by default; `skippedDirectories` changes the list). Pass one instance to `FileOperationsToolCollection` and to `ShellToolCollection` so both work in the same folder; the shell's `workingDirectory` stays inside it, symbolic links resolved. `ShellToolCollection()` with no argument uses a workspace on the current directory; the file collection always needs one.
- **Searching.** `FindFiles` matches names with a glob (a pattern with no `/` matches at any depth), newest first, at most 100. `SearchFiles` matches a .NET regular expression against lines and answers with files, matching lines, or counts per file. Both skip the skipped folders and never follow a symbolic link, and a result over the read limit or the file cap says so. Binary files, files that are not UTF-8 and files over 10 MB are counted, not searched.

- **Path sandboxing.** Every tool canonicalizes paths and resolves symbolic links, including a link that leads through another link. Paths containing `..`, absolute paths outside the base directory, sibling directory prefix traps, and symbolic links pointing outside are refused with an error. The base directory is required and must exist. The guard is not a container: a hard link to a file outside the base directory is followed, and a link swapped in between the check and the file operation is not caught.
- **Links.** `DeleteFile` and `MoveFile` act on a symbolic link itself and leave its target alone; the other tools act on the target.
- **Read limit.** A reply from `ReadFile`, `ListDirectory`, `FindFiles` or `SearchFiles` is capped at 51,200 characters (configurable with the builder's `WithMaxReadCharacters`; the shell counts characters too). A read or listing over the limit returns the first page and says how to continue (`continue with startLine=413`, or how many entries were left out); it never refuses a large file. Reads stream line by line, so a large file is not held in memory. Every line is returned as `N: text`, and a line over 2,000 characters is cut with a marker.
- **Exact-match editing.** `EditFile` replaces `oldText` only when it occurs exactly once in the file, or every occurrence with `replaceAll`. A failed match answers with the closest lines in the file and their numbers. Line breaks match across Windows (`\r\n`) and POSIX (`\n`) formats, the replacement is written in the file's prevailing line ending, and the lines outside the match keep theirs. The reply shows the edited lines with their numbers.
- **Approvals.** Approval belongs to a whole tool, never to one call, so the tools are split along it: `WriteFile` (overwrites) and `DeleteFile` require host approval by default (`RequiresApproval = true`); `CreateFile`, `EditFile` and `MoveFile` run unattended unless the agent builder names them in `RequireApproval(...)`. `EditFile` can change every line of a file, so a host that wants every change approved should name it too. `keepBackups: true` copies a file to `.pinkrooster/backups` before `EditFile`, `WriteFile` or `DeleteFile` changes it (the reply names the copy; nothing is changed if the copy cannot be made; copies older than a week or beyond the newest hundred are removed), so an unapproved change can be undone: by hand, or with [`UndoAsync`](#undo).
- **Read-only.** `FileReadToolCollection` is the base class of `FileOperationsToolCollection` and holds only `FindFiles`, `SearchFiles`, `ListDirectory` and `ReadFile`; give an agent that must not change files this one.
- **Safety.** `CreateFile` and `MoveFile` fail when the destination file exists, preventing silent overwrites. `CreateFile` cannot overwrite even when the file appears after its check. Binary and non-UTF-8 files are refused by `ReadFile` and `EditFile`, so an edit never rewrites text it could not decode. Files are written as UTF-8; `EditFile` and `WriteFile` keep a byte order mark the file already had.

### Undo

With backups on, the collection records every change it makes, per session, and the host can take a user message's changes back:

```csharp
using Microsoft.Agents.AI;
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;

FileOperationsToolCollection files = new FileOperationsToolCollectionBuilder().InWorkspace(new Workspace(repoRoot)).KeepBackups().Build();
AgentSession session = await agent.CreateSessionAsync();

files.BeginUndoUnit(session);                      // before each prompt you send
await agent.RunAsync("Rename the Order class to Purchase.", session);

FileUndoResult undone = await files.UndoAsync(session);
Console.WriteLine(undone);                         // what was restored, and what was left and why
```

- **What it takes back.** Every change the file tools made since the last `BeginUndoUnit` on that session: an edited, overwritten or
  deleted file gets its previous content back byte for byte, line endings and byte order mark included; a created file is removed;
  a moved file is moved back. The next `UndoAsync` takes back the message before that. A message that changed no file is passed
  over. There is no redo.
- **What it does not.** A shell command's effects stay: undo knows only what the file tools did. Directories a tool created stay.
  A link that was deleted is not put back, because no copy of a link is kept.
- **One unit is one prompt.** Call `BeginUndoUnit` before each prompt, not for a message posted to a run in progress, which
  belongs to that run's prompt. A host that never calls it has one unit that holds every change of the session.
- **A file someone else changed since is left alone.** Undo compares the file with what the tool left; when it differs, the file
  is skipped and named, and the other files of the message are restored. The same goes for a file whose copy is no longer in
  `.pinkrooster/backups` (copies are kept for a week, and the newest hundred).
- **The conversation is not rewound.** The model still believes its changes are there, so put the result's text
  (`undone.ToString()`) in front of the next prompt. `undone.NothingToUndo` says that no change was on record.
- **It is a host call, not a tool**, to be made between runs. Without backups `UndoAsync` throws and names `KeepBackups()`.
- **The record lives in the session**, one small entry per change, so it is saved and restored with the session and undo still
  works after a restart. Two sessions on one collection never undo each other's changes.

### Tools

- `FindFiles(string pattern, string? path = null)`: Finds files by glob, newest first.
- `SearchFiles(string pattern, string? path = null, string? include = null, SearchMode mode = Files, bool ignoreCase = false, int limit = 100)`: Searches file text with a regular expression; `mode` is `Files`, `Lines` or `Counts`.
- `ListDirectory(string? path = null)`: Lists direct entries: directories as `name/` first, then files as `name (size in bytes)`.
- `ReadFile(string path, int? startLine = null, int? endLine = null)`: Reads numbered lines (`N: text`), with a header that says whether more lines follow and which `startLine` continues.
- `CreateFile(string path, string content)`: Creates a new file and missing parent directories. Fails if the file exists.
- `WriteFile(string path, string content)`: Overwrites an existing file. Requires approval. Fails if the file does not exist.
- `EditFile(string path, string oldText, string newText, bool replaceAll = false)`: Replaces a unique match (or every match) and returns the changed lines.
- `MoveFile(string sourcePath, string destinationPath)`: Moves or renames a file. Fails if the destination exists.
- `DeleteFile(string path)`: Deletes an existing file. Requires approval.

## AskUserQuestionToolCollection

Adds an `AskUserQuestion` tool: up to 4 questions per call, each with 2 to 4 options and a header of at most 12 characters.
The collection renders nothing. Your callback shows the questions and returns one `UserQuestionAnswer` per question, in order:

```csharp
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

new AskUserQuestionToolCollection(async (questions, cancellationToken) =>
    [.. questions.Select(q => new UserQuestionAnswer([q.Options[0].Label]))]);
```

The callback holds the run open until it returns. That fits console and desktop apps; a web host cannot hold a request open that
long. [`samples/PinkRooster.Samples.CodingAgent`](https://github.com/pinkroosterai/PinkRooster/blob/main/samples/PinkRooster.Samples.CodingAgent/README.md) shows a
real callback drawn with Spectre.Console.

## DateTimeToolCollection

Adds `GetDateTimeAfter`, `GetDayOfWeek` and `GetDaysBetween`, and sends the current day, date and time before every model call, so
the model knows the time without asking. `new DateTimeToolCollection()` reads the system clock in the local time zone; `new DateTimeToolCollection(timeProvider, defaultTimeZone)`
takes another clock and an IANA or Windows time zone ID.

## TaskListToolCollection

Gives the agent a task list for work of three or more steps, as four tools that take batches: `TaskAdd`, `TaskUpdate`,
`TaskRemove` and `TaskGet`. Their replies are one line, because the list itself is sent before every model call in a compact form:
the counts, the open tasks with the one in progress first, then those that can start, then those that wait, and only a count of the
completed ones. A list of 50 tasks adds under 1,000 characters to a call (a test holds the limit); `TaskGet` with no ids reads everything.

```csharp
using PinkRooster.Agents;
using PinkRooster.ToolCollections.BuiltIn.TaskLists;

var agent = chatClient
    .CreateAgent()
    .WithRole("You plan and carry out multi-step jobs.")
    .WithTools(new TaskListToolCollection(new JsonFileTaskListStorage("tasks/session-42.json")))
    .Build();
```

- **One collection, one list, unless you say per session.** By default a collection keeps one list, shared by every agent and session
  it is given to. `new TaskListToolCollectionBuilder().PerSession().Build()` gives each session its own list instead, kept in the
  session and not in a storage, so one agent serves many conversations and a list is saved and restored with its session.
  `tasks.GetTasks(session)` returns a copy of a session's list outside a run, for a host that shows it.
- **Storage.** `ITaskListStorage` has two methods, `LoadAsync` and `SaveAsync`. The collection reads once, keeps the
  list in memory and saves after every change. `JsonFileTaskListStorage` keeps a JSON file, written through a temporary file so a
  crash leaves no half-written list; it is for one process, and two processes on one file overwrite each other. A file it cannot
  read is moved to `<path>.corrupt` and the list starts empty. `InMemoryTaskListStorage` keeps nothing past the process.
- **Rules, checked in code.** A task cannot be `in_progress` or `completed` while a task it waits on (`blockedBy`) is open, tasks
  cannot wait on each other in a loop, and only one task is `in_progress` at a time (unless the builder's `AllowMultipleInProgress()` is set). A call
  is all-or-nothing and is checked as it will be after the whole call, so "complete #1 and start #2" in one call is valid. A new
  task in `TaskAdd` can wait on another of the same call with `"@1"`, the first task of the call. Ids are never reused.
- **Options.** `TaskListToolCollectionBuilder` sets `WithMaxVisibleItems` (15) and `WithMaxSubjectLength` (80) for the injected view, switches the view
  off with `WithoutContext()`, and replaces it with `FormatContextWith`. `new TaskListToolCollection()` keeps the list in memory and
  `new TaskListToolCollection(storage)` in a storage, both with the defaults.
- **Prompt caching.** With the agent builder the list arrives as a message at the end of each request, so the start of the prompt
  stays the same. A `ToolCollectionContextProvider` sends it as instructions, which change whenever the list changes; prefer the
  builder for this collection.

## ImageToolCollection

Adds one tool, `QueryImage`, that lets an agent ask about image files in the workspace: a screenshot, a photo, a chart, a scan. The
images and the question go to a second chat client, of a model that accepts images, and its answer comes back as text. So the
model that runs the agent does not have to see, and no image enters the agent's own conversation.

```csharp
using PinkRooster.ToolCollections.BuiltIn;
using PinkRooster.ToolCollections.BuiltIn.Images;

ImageToolCollection images = new ImageToolCollectionBuilder()
    .InWorkspace(new Workspace(repoRoot))
    .WithVisionClient(visionClient)
    .WithMaxImagesPerCall(2)
    .WithTimeout(TimeSpan.FromSeconds(60))
    .Build();
```

`new ImageToolCollection(workspace, visionClient)` does the same with the defaults. `visionClient` is any `IChatClient` of a model
that accepts images; it can be the agent's own client. `QueryImage(paths, question)` takes one or more paths. With several, each
image is labelled `Image 1:`, `Image 2:` in the order given, which is how the agent compares them.

- **One request per call, and it is yours.** The vision client gets a system message, then the images followed by the question:
  no tools, none of the agent's history, no chat options. Set the model and the answer length on the client you pass, for example
  with `visionClient.AsBuilder().ConfigureOptions(options => options.MaxOutputTokens = 800).Build()`, and wrap it in a
  `DelegatingChatClient` to count its tokens: they are not part of the agent's own usage. There is no cache.
- **A file is sent as it is.** Nothing is decoded, turned, scaled or converted, so the package needs no imaging library. A file is
  accepted when its content, not its name, is JPEG, PNG, GIF or WebP, and it is at most 3,750,000 bytes (5 MB once encoded, the
  tightest limit a provider documents; `WithMaxImageBytes` changes it). Anything else is refused with an error that says what the
  file is. What a provider makes of a very large image, or of a photo stored rotated, is the provider's: Anthropic and OpenAI
  document that they scale a large image down and do not read the rotation a camera stored.
- **Limits.** At most 6 images per call (`WithMaxImagesPerCall`), and no time limit unless you set one (`WithTimeout`); when it
  passes, the tool returns an error and the agent goes on. An answer longer than 51,200 characters is cut, with a note. A call
  with one refused image sends nothing.
- **Failures are text for the model.** A provider's refusal, such as a rate limit or a model that cannot see, comes back as
  `Error: the vision model call failed: ...`. The collection cannot tell beforehand whether the client's model accepts images.
- **Instructions inside an image.** The vision call's system message and a constraint the collection adds to the agent's prompt
  both say that text found in an image is content, never an instruction. That lowers the risk and does not remove it: keep
  approvals on the tools that change things.

## Tool kinds

Every tool here declares a [kind](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.ToolCollections.md#tool-kinds), read with `tool.GetKind()`:

| Kind | Tools |
|---|---|
| `Read` | `FindFiles`, `SearchFiles`, `ListDirectory`, `ReadFile`, `TaskGet`, `QueryImage`, and the date and time tools |
| `State` | `TaskAdd`, `TaskUpdate`, `TaskRemove`, `AskUserQuestion` |
| `Edit` | `CreateFile`, `WriteFile`, `EditFile`, `MoveFile`, `DeleteFile` |
| `Execute` | `RunShell` |

A test in the repository fails for a tool added here without one.

## Running these tools in the background

An agent built with `AgentBuilder` can let the model start a tool as a
[background task](https://github.com/pinkroosterai/PinkRooster/blob/main/docs/PinkRooster.Agents.md#background-tools). Allowing a tool
says it may run at the same time as other calls on the same collection instance. For the collections here, from reading their code
(none was tested under concurrent calls):

| Collection | Safe to allow | Why |
|---|---|---|
| `ShellToolCollection` (`RunShell`) | Yes | Every call starts a fresh shell and the collection keeps nothing between calls. It is the tool worth allowing: builds and test runs take long. |
| `FileReadToolCollection` (`ListDirectory`, `ReadFile`, `FindFiles`, `SearchFiles`) | Yes | The tools only read, and the collection keeps nothing between calls. |
| `FileOperationsToolCollection` (`CreateFile`, `WriteFile`, `EditFile`, `MoveFile`, `DeleteFile`) | Not on the same file | Nothing locks a file while a tool reads and rewrites it, so two calls that change one file at the same time can lose a change. |
| `TaskListToolCollection` | Yes | Its changes take turns behind one lock. |
| `DateTimeToolCollection` | Yes | It keeps nothing between calls. |
| `ImageToolCollection` (`QueryImage`) | Yes | It keeps nothing between calls. A call waits for the vision model, so the background can pay off. |
| `AskUserQuestionToolCollection` | Only if your question function is | The collection keeps nothing; two questions at once reach your function at once. `AgentConsole` asks one at a time. |

Every tool here except the date tools takes a `CancellationToken`, so a background task of it stops when it is cancelled or times out.
`RunShell` and `QueryImage` can run long enough for the background to pay off; the others answer at once.

## Feedback and license

Report a bug or ask a question on [GitHub Issues](https://github.com/pinkroosterai/PinkRooster/issues). The package is released under the [MIT license](https://github.com/pinkroosterai/PinkRooster/blob/main/LICENSE).
