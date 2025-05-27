using PlanescapeStackOperator.Entities;

namespace PlanescapeStackOperator.Services;

public interface IVaultService
{
    Task InitializeVaultAsync(PlanescapeStack stack, CancellationToken cancellationToken);
    Task ConfigureVaultAsync(PlanescapeStack stack, CancellationToken cancellationToken);
    Task ConfigurePostgresqlDatabaseAsync(PlanescapeStack stack, CancellationToken cancellationToken);
    Task ConfigureJobVaultAccessAsync(PlanescapeStack stack, PlanescapeJob job, CancellationToken cancellationToken);
    Task CleanupVaultAsync(PlanescapeStack stack, CancellationToken cancellationToken);
    Task<bool> IsVaultReadyAsync(string @namespace, string releaseName, CancellationToken cancellationToken);
    Task<string> GetVaultTokenAsync(string @namespace, string releaseName, CancellationToken cancellationToken);
    Task StoreSecretAsync(string @namespace, string path, string key, string value, CancellationToken cancellationToken);
    Task<string> GetSecretAsync(string @namespace, string path, string key, CancellationToken cancellationToken);
} 