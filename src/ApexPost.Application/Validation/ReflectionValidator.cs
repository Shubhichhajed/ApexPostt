using ApexPost.Domain;

namespace ApexPost.Application;

public static class ReflectionValidator
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo[]> RequiredProperties = new();
    public static IReadOnlyList<string> Validate(object instance)
    {
        var props = RequiredProperties.GetOrAdd(instance.GetType(), type => type.GetProperties().Where(p => Attribute.IsDefined(p, typeof(RequiredValueAttribute))).ToArray());
        return props.Where(p => p.GetValue(instance) is null || p.GetValue(instance) is string s && string.IsNullOrWhiteSpace(s)).Select(p => $"{p.Name} is required.").ToArray();
    }
}
