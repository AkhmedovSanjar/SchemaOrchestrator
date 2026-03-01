namespace SchemaOrchestrator.Subscribers.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OnUpgradeFailedAttribute : Attribute;
