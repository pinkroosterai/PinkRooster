namespace PinkRooster.Agents.Shared;

/// <summary>Walks an agent class and its base types, for reading attributes with <c>inherit: false</c> in a fixed order.</summary>
internal static class TypeChain
{
    /// <summary>The type and its base types, base types first: what a base declares reads before what a derived class adds or replaces.</summary>
    public static List<Type> BaseFirst(Type type)
    {
        List<Type> chain = [];
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            chain.Add(current);
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>The error for an attribute that holds blank text, naming the class it sits on and the fix.</summary>
    public static InvalidOperationException Blank(Type attributeType, Type type, Type declaredOn)
    {
        string attribute = attributeType.Name.Replace("Attribute", "");
        string where = declaredOn == type ? $"agent class '{type.Name}'" : $"'{declaredOn.Name}', a base of agent class '{type.Name}'";
        return new InvalidOperationException($"A [{attribute}] on {where} holds blank text; give it text, or remove the attribute.");
    }
}
