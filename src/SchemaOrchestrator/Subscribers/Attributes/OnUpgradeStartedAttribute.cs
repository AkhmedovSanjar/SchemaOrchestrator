namespace SchemaOrchestrator.Subscribers.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OnUpgradeStartedAttribute : Attribute;
