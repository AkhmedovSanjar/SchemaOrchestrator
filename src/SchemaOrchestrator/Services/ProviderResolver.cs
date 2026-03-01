using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Abstractions;

namespace SchemaOrchestrator.Services;

public sealed class ProviderResolver
{
    private readonly IReadOnlyList<IProviderAdapter> _adapters;

    public ProviderResolver(IEnumerable<IProviderAdapter> adapters)
    {
        _adapters = adapters.ToArray();
    }

    public IProviderAdapter Resolve(DbContext dbContext)
    {
        var providerName = dbContext.Database.ProviderName;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException("Unable to resolve EF Core provider name.");
        }

        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(providerName));
        return adapter ?? throw new NotSupportedException($"No SchemaOrchestrator adapter registered for provider '{providerName}'.");
    }
}
