using System.Reflection;
using Microsoft.Agents.AI;
using PinkRooster.Agents;

namespace PinkRooster.Repository.Tests;

/// <summary>The public API of <c>PinkRooster.Agents</c> keeps MAF's experimental types (<c>MAAI001</c>) inside, because the step loop runs on them.</summary>
public sealed class PublicApiTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void NoPublicTypeOrMember_OfAgents_ExposesAnExperimentalMafType()
    {
        List<string> offenders = [];
        foreach (Type type in typeof(AgentBuilder).Assembly.GetExportedTypes())
        {
            if (IsExperimental(type) || type.BaseType is { } baseType && IsExperimental(baseType) || type.GetInterfaces().Any(IsExperimental))
            {
                offenders.Add($"{type.FullName} (type)");
            }
            foreach (MemberInfo member in type.GetMembers(Declared).Where(IsExposed))
            {
                foreach (Type used in TypesOf(member))
                {
                    if (IsExperimental(used))
                    {
                        offenders.Add($"{type.FullName}.{member.Name} uses {used.FullName}");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheCheck_SeesAnExperimentalType()
    {
        Assert.True(IsExperimental(typeof(LoopAgent)));
    }

    // Public, or protected on a type a caller can derive from.
    private static bool IsExposed(MemberInfo member) => member switch
    {
        MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => (property.GetMethod?.IsPublic ?? false) || (property.GetMethod?.IsFamily ?? false) || (property.GetMethod?.IsFamilyOrAssembly ?? false)
            || (property.SetMethod?.IsPublic ?? false) || (property.SetMethod?.IsFamily ?? false),
        EventInfo => true,
        _ => false
    };

    private static IEnumerable<Type> TypesOf(MemberInfo member)
    {
        IEnumerable<Type> raw = member switch
        {
            MethodInfo info => [.. info.GetParameters().Select(parameter => parameter.ParameterType), info.ReturnType],
            MethodBase method => [.. method.GetParameters().Select(parameter => parameter.ParameterType)],
            FieldInfo field => [field.FieldType],
            PropertyInfo property => [property.PropertyType],
            EventInfo evt => evt.EventHandlerType is { } handler ? [handler] : [],
            _ => []
        };
        return raw.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        Type element = type.HasElementType ? type.GetElementType()! : type;
        yield return element;
        if (element.IsGenericType)
        {
            foreach (Type argument in element.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }

    private static bool IsExperimental(Type type) =>
        type.GetCustomAttributesData().Any(data => data.AttributeType.Name == "ExperimentalAttribute"
            && data.ConstructorArguments.Count > 0 && data.ConstructorArguments[0].Value as string == "MAAI001");
}
