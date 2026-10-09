using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace PinkRooster.Repository.Tests;

/// <summary>
/// Keeps a text listing of each package's public API under <c>PublicApi/</c>, so a change to it, wanted or not, shows up in the diff of
/// the change that made it. The API may change freely before 1.0; this only makes the change visible.
/// </summary>
/// <remarks>To accept a change, run the tests once with the environment variable <c>PINKROOSTER_UPDATE_PUBLIC_API=1</c> and commit the files.</remarks>
public sealed class PublicApiSnapshotTests
{
    private const string UpdateVariable = "PINKROOSTER_UPDATE_PUBLIC_API";

    public static TheoryData<string> Packages => new(
        "PinkRooster.Agents",
        "PinkRooster.OpenAI.Reasoning",
        "PinkRooster.SpectreConsole",
        "PinkRooster.ToolCollections",
        "PinkRooster.ToolCollections.BuiltIn",
        "PinkRooster.ToolCollections.Mcp");

    [Theory]
    [MemberData(nameof(Packages))]
    public void ThePublicApi_IsTheOneOnFile(string package)
    {
        string actual = Describe(Assembly.Load(package));
        string path = Path.Combine(FindRepositoryRoot(), "tests", "PinkRooster.Repository.Tests", "PublicApi", package + ".txt");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        string expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
        if (expected == actual)
        {
            return;
        }

        HashSet<string> before = [.. expected.Split('\n')];
        HashSet<string> after = [.. actual.Split('\n')];
        IEnumerable<string> changes = before.Except(after).Select(line => "- " + line).Concat(after.Except(before).Select(line => "+ " + line)).Take(40);
        Assert.Fail(
            $"The public API of {package} differs from PublicApi/{package}.txt. If the change is meant, run the tests once with {UpdateVariable}=1 " +
            $"and commit the file; if not, the type or member below became public, or changed, by accident.\n{string.Join("\n", changes)}");
    }

    private static string Describe(Assembly assembly)
    {
        StringBuilder text = new();
        foreach (Type type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            text.Append(Kind(type)).Append(' ').Append(Name(type, qualified: true));
            string[] bases =
            [
                .. type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum) && baseType != typeof(MulticastDelegate) ? new[] { Name(baseType) } : [],
                .. type.GetInterfaces().Except(type.BaseType?.GetInterfaces() ?? []).Select(item => Name(item)).Order(StringComparer.Ordinal)
            ];
            if (bases.Length > 0)
            {
                text.Append(" : ").AppendJoin(", ", bases);
            }
            text.Append('\n');

            IEnumerable<string> members = type.IsEnum
                ? Enum.GetNames(type).Select(name => name)
                : type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(member => !member.Name.StartsWith('<') && member.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
                    .Select(Member).OfType<string>().Order(StringComparer.Ordinal);
            foreach (string member in members)
            {
                text.Append("    ").Append(member).Append('\n');
            }
        }
        return text.ToString();
    }

    private static string Kind(Type type) =>
        type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct"
        : (type.IsAbstract && type.IsSealed ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : "") + "class";

    private static string? Member(MemberInfo member) => member switch
    {
        ConstructorInfo { IsStatic: false } constructor when Access(constructor) is { } access => $"{access}.ctor({Parameters(constructor)})",
        MethodInfo { IsSpecialName: false } method when Access(method) is { } access =>
            $"{access}{Modifiers(method)}{method.Name}{Generic(method.GetGenericArguments())}({Parameters(method)}) : {Name(method.ReturnType)}",
        PropertyInfo property when Accessors(property) is { Length: > 0 } accessors =>
            $"{Modifiers(property.GetMethod ?? property.SetMethod!)}{property.Name} : {Name(property.PropertyType)} {{ {accessors} }}",
        FieldInfo field when Access(field.IsPublic, field.IsFamily || field.IsFamilyOrAssembly) is { } access =>
            $"{access}{(field.IsLiteral ? "const " : field.IsStatic ? "static " : "")}{field.Name} : {Name(field.FieldType)}",
        _ => null
    };

    private static string? Access(MethodBase method) => Access(method.IsPublic, method.IsFamily || method.IsFamilyOrAssembly);

    private static string? Access(bool isPublic, bool isProtected) => isPublic ? "" : isProtected ? "protected " : null;

    private static string Modifiers(MethodInfo method) =>
        method.IsStatic ? "static " : method.IsAbstract ? "abstract " : method.IsVirtual && !method.IsFinal && method.GetBaseDefinition() != method ? "override "
        : method.IsVirtual && !method.IsFinal ? "virtual " : "";

    private static string Accessors(PropertyInfo property) =>
        string.Join(" ", new[] { (property.GetMethod, "get;"), (property.SetMethod, "set;") }
            .Where(accessor => accessor.Item1 is not null && Access(accessor.Item1) is not null)
            .Select(accessor => Access(accessor.Item1!) + accessor.Item2));

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter =>
            (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null || parameter.GetCustomAttribute<ParamCollectionAttribute>() is not null ? "params " : "")
            + Name(parameter.ParameterType) + " " + parameter.Name + (parameter.IsOptional ? " = default" : "")));

    private static string Generic(Type[] arguments) => arguments.Length == 0 ? "" : $"<{string.Join(", ", arguments.Select(argument => Name(argument)))}>";

    private static string Name(Type type, bool qualified = false)
    {
        if (type.IsByRef)
        {
            return "ref " + Name(type.GetElementType()!);
        }
        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[]";
        }
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying) + "?";
        }
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        string name = type.IsNested ? Name(type.DeclaringType!, qualified: false) + "." + type.Name : type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
        {
            name = name[..tick];
        }
        if (type.IsGenericType)
        {
            name += Generic(type.GetGenericArguments());
        }
        return qualified && type.Namespace is { } scope ? scope + "." + name : name;
    }

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
