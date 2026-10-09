using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PinkRooster.Repository.Tests;

/// <summary>
/// Compiles every <c>csharp</c> code block of the README and <c>docs/*.md</c> from the text it prints, with the <c>using</c> lines it prints and
/// the implicit usings a new project has: a reader who copies the block into an app gets code that compiles. The names a block takes from its
/// surroundings (<c>chatClient</c>, <c>store</c>) come from <see cref="DocSnippetHarness"/>.
/// </summary>
public sealed partial class DocSnippetTests
{
    private static readonly string Root = FindRepositoryRoot();

    public static IEnumerable<TheoryDataRow<string, int>> Snippets() =>
        DocumentFiles().SelectMany(file => Fences(File.ReadAllText(file)).Select((_, index) => new TheoryDataRow<string, int>(Path.GetRelativePath(Root, file).Replace('\\', '/'), index + 1)));

    [Theory]
    [MemberData(nameof(Snippets))]
    public void EveryPrintedSnippet_CompilesWithItsPrintedUsings(string file, int number)
    {
        string snippet = Fences(File.ReadAllText(Path.Combine(Root, file)))[number - 1];

        string[] errors = DocSnippetHarness.Errors(snippet);

        Assert.True(errors.Length == 0, $"{file}, csharp block {number}, does not compile with the using lines it prints:\n{string.Join("\n", errors)}\n--- block ---\n{snippet}");
    }

    [Fact]
    public void ABlockMissingOneOfItsUsings_DoesNotCompile()
    {
        string quickstart = Fences(File.ReadAllText(Path.Combine(Root, "README.md"))).Single(block => block.Contains("ShellToolCollection") && block.Contains("RunAsync") && block.StartsWith("using ", StringComparison.Ordinal));
        string without = string.Join("\n", quickstart.Split('\n').Where(line => !line.StartsWith("using PinkRooster.ToolCollections.BuiltIn", StringComparison.Ordinal)));

        Assert.Empty(DocSnippetHarness.Errors(quickstart));
        Assert.NotEmpty(DocSnippetHarness.Errors(without));
    }

    private static IEnumerable<string> DocumentFiles() =>
        [Path.Combine(Root, "README.md"), .. Directory.GetFiles(Path.Combine(Root, "docs"), "PinkRooster.*.md").Order()];

    private static List<string> Fences(string markdown) =>
        [.. CSharpFence().Matches(markdown.ReplaceLineEndings("\n")).Select(match => match.Groups["code"].Value.TrimEnd())];

    [GeneratedRegex("^```csharp\n(?<code>.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex CSharpFence();

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PinkRooster.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("PinkRooster.slnx not found above the test output folder.");
    }
}

/// <summary>Wraps a documented snippet so it can be compiled alone: its statements become one async method, its types sit beside it, and what it takes from context is declared.</summary>
internal static class DocSnippetHarness
{
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    // Names a snippet may use without declaring them; each is declared only when the snippet does not declare it itself.
    private static readonly (string Name, string Type)[] Ambient =
    [
        ("chatClient", "global::Microsoft.Extensions.AI.IChatClient"),
        ("visionClient", "global::Microsoft.Extensions.AI.IChatClient"),
        ("store", "ITicketStore"),
        ("docs", "global::PinkRooster.ToolCollections.Mcp.McpToolCollection"),
        ("repoRoot", "string"),
        ("generatedFunctions", "IEnumerable<global::Microsoft.Extensions.AI.AIFunction>"),
        ("agent", "global::Microsoft.Agents.AI.AIAgent"),
        ("reviewer", "global::PinkRooster.Agents.DeclaredAgent"),
        ("chatResponse", "global::Microsoft.Extensions.AI.ChatResponse"),
        ("gitServer", "global::ModelContextProtocol.Client.McpClient"),
        ("messages", "IEnumerable<global::Microsoft.Extensions.AI.ChatMessage>"),
        ("refunds", "global::Microsoft.Agents.AI.Workflows.ExecutorBinding"),
        ("support", "global::Microsoft.Agents.AI.Workflows.ExecutorBinding"),
    ];

    private const string Stubs = """
        public interface ITicketStore
        {
            Task<string> DescribeAsync(string number);
            Task<string> CloseAsync(string number);
            Task<int> CountOpenAsync(CancellationToken cancellationToken);
        }

        public static class Git
        {
            public static string Branch(string root) => root;
        }
        """;

    // A stub is added only when the snippet does not declare a type of that name.
    private static readonly (string Name, string Source)[] ConditionalStubs =
    [
        ("TicketTools", "public sealed class TicketTools(ITicketStore store) : global::PinkRooster.ToolCollections.ToolCollection;"),
        ("ReviewerAgent", "public sealed class ReviewerAgent(global::Microsoft.Extensions.AI.IChatClient chatClient) : global::PinkRooster.Agents.DeclaredAgent(chatClient);"),
        ("EscalationAgent", "public sealed class EscalationAgent(global::Microsoft.Agents.AI.AIAgent inner) : global::Microsoft.Agents.AI.DelegatingAIAgent(inner);"),
    ];

    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        string[] trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        return [.. trusted.Concat(AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0).Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path))];
    });

    public static string[] Errors(string snippet) =>
        [.. Compile(snippet, usingsOverride: null).Compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")];

    internal static (CSharpCompilation Compilation, SyntaxTree Tree) Compile(string snippet, string? usingsOverride)
    {
        CompilationUnitSyntax root = SyntaxFactory.ParseCompilationUnit(snippet);
        string usings = usingsOverride ?? string.Join("\n", root.Usings.Select(item => item.ToFullString().Trim()));
        // Only what the snippet's own statements declare hides an ambient name; a class's constructor parameter does not.
        GlobalStatementSyntax[] statementNodes = [.. root.Members.OfType<GlobalStatementSyntax>()];
        string[] declared = [.. statementNodes.SelectMany(statement => statement.DescendantNodes().OfType<VariableDeclaratorSyntax>().Select(node => node.Identifier.Text)
            .Concat(statement.DescendantNodes().OfType<ParameterSyntax>().Select(node => node.Identifier.Text))
            .Concat(statement.DescendantNodes().OfType<SingleVariableDesignationSyntax>().Select(node => node.Identifier.Text)))];
        string parameters = string.Join(", ", Ambient.Where(item => !declared.Contains(item.Name)).Select(item => $"{item.Type} {item.Name}"));

        IEnumerable<string> types = root.Members.Where(member => member is not GlobalStatementSyntax).Select(member => member.ToFullString());
        IEnumerable<string> statements = root.Members.OfType<GlobalStatementSyntax>().Select(member => member.ToFullString());

        string[] declaredTypes = [.. root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(node => node.Identifier.Text)];
        string conditional = string.Join("\n", ConditionalStubs.Where(stub => !declaredTypes.Contains(stub.Name)).Select(stub => stub.Source));
        string helpers = snippet.Contains("AskTheUserAsync", StringComparison.Ordinal) && !declared.Contains("AskTheUserAsync")
            ? "static global::System.Threading.Tasks.Task<bool> AskTheUserAsync(global::Microsoft.Extensions.AI.FunctionCallContent call, global::System.Threading.CancellationToken cancellationToken) => global::System.Threading.Tasks.Task.FromResult(true);"
            : "";

        string source = $$"""
            {{usings}}

            namespace SnippetHarness
            {
                {{string.Join("\n", types)}}

                internal static class Snippet
                {
                    internal static async Task Run({{parameters}})
                    {
                        {{helpers}}
                        {{string.Join("\n", statements)}}
                        await Task.CompletedTask;
                    }
                }

                {{Stubs}}

                {{conditional}}
            }
            """;

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(ImplicitUsings, new CSharpParseOptions(LanguageVersion.Latest)), tree],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                // The loop types are experimental in MAF; the docs that use them tell the reader to suppress MAAI001.
                .WithSpecificDiagnosticOptions([new KeyValuePair<string, ReportDiagnostic>("MAAI001", ReportDiagnostic.Suppress)]));
        return (compilation, tree);
    }
}
