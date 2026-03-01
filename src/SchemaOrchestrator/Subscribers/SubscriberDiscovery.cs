using System.Reflection;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Subscribers.Attributes;

namespace SchemaOrchestrator.Subscribers;

internal static class SubscriberDiscovery
{
    public static IReadOnlyList<ISchemaUpgradeSubscriber> Discover(params Assembly[] assemblies)
    {
        var result = new List<ISchemaUpgradeSubscriber>();
        var attrs = new[]
        {
            typeof(OnUpgradeStartedAttribute),
            typeof(BeforeOperationAttribute),
            typeof(AfterOperationAttribute),
            typeof(OnUpgradeCompletedAttribute),
            typeof(OnUpgradeFailedAttribute)
        };

        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in assembly.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                var methods = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => attrs.Any(a => m.GetCustomAttribute(a) is not null))
                    .ToList();

                if (methods.Count == 0)
                {
                    continue;
                }

                var instance = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException($"Could not create subscriber type '{type.FullName}'.");
                result.Add(new ReflectedSchemaUpgradeSubscriber(instance, methods));
            }
        }

        return result;
    }
}
