using PlanescapeStackOperator.Entities;

namespace PlanescapeStackOperator.Services;

public interface IStackHealthService
{
    Task<Dictionary<string, ComponentStatus>> CheckStackHealthAsync(PlanescapeStack stack, CancellationToken cancellationToken);
    Task<bool> IsComponentReadyAsync(string @namespace, string componentName, string componentType, CancellationToken cancellationToken);
    Task<ComponentStatus> GetComponentStatusAsync(string @namespace, string componentName, string componentType, CancellationToken cancellationToken);
} 