using k8s.Models;
using KubeOps.Abstractions.Finalizer;
using Microsoft.Extensions.Logging;
using PlanescapeStackOperator.Entities;
using PlanescapeStackOperator.Services;

namespace PlanescapeStackOperator.Finalizer;

public interface IPlanescapeStackFinalizer
{
    Task FinalizeAsync(PlanescapeStack entity, CancellationToken cancellationToken);
}

public class PlanescapeStackFinalizer : IEntityFinalizer<PlanescapeStack>, IPlanescapeStackFinalizer
{
    private readonly ILogger<PlanescapeStackFinalizer> _logger;
    private readonly IHelmService _helmService;
    private readonly IVaultService _vaultService;

    public PlanescapeStackFinalizer(
        ILogger<PlanescapeStackFinalizer> logger,
        IHelmService helmService,
        IVaultService vaultService)
    {
        _logger = logger;
        _helmService = helmService;
        _vaultService = vaultService;
    }

    public async Task FinalizeAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizing PlanescapeStack {Name} in namespace {Namespace}",
            entity.Metadata.Name, entity.Metadata.NamespaceProperty);

        try
        {
            // Clean up resources in reverse order of creation
            await CleanupJenkinsAsync(entity, cancellationToken);
            await CleanupPostgresqlAsync(entity, cancellationToken);
            await CleanupVaultAsync(entity, cancellationToken);

            _logger.LogInformation("Successfully finalized PlanescapeStack {Name}", entity.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finalizing PlanescapeStack {Name}", entity.Metadata.Name);
            throw;
        }
    }

    private async Task CleanupJenkinsAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        if (entity.Spec.Components.Jenkins?.Enabled != true)
        {
            _logger.LogDebug("Jenkins not enabled for stack {Name}, skipping cleanup", entity.Metadata.Name);
            return;
        }

        var releaseName = $"{entity.Metadata.Name}-jenkins";
        
        try
        {
            var releaseExists = await _helmService.IsReleaseInstalledAsync(entity.Metadata.NamespaceProperty, releaseName);
            if (releaseExists)
            {
                _logger.LogInformation("Uninstalling Jenkins release {ReleaseName}", releaseName);
                await _helmService.UninstallReleaseAsync(entity.Metadata.NamespaceProperty, releaseName);
                _logger.LogInformation("Jenkins release {ReleaseName} uninstalled successfully", releaseName);
            }
            else
            {
                _logger.LogInformation("Jenkins release {ReleaseName} does not exist, skipping uninstall", releaseName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up Jenkins release {ReleaseName}, continuing with cleanup", releaseName);
        }
    }

    private async Task CleanupPostgresqlAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        if (entity.Spec.Components.Postgresql?.Enabled != true)
        {
            _logger.LogDebug("PostgreSQL not enabled for stack {Name}, skipping cleanup", entity.Metadata.Name);
            return;
        }

        var releaseName = $"{entity.Metadata.Name}-postgresql";
        
        try
        {
            var releaseExists = await _helmService.IsReleaseInstalledAsync(entity.Metadata.NamespaceProperty, releaseName);
            if (releaseExists)
            {
                _logger.LogInformation("Uninstalling PostgreSQL release {ReleaseName}", releaseName);
                await _helmService.UninstallReleaseAsync(entity.Metadata.NamespaceProperty, releaseName);
                _logger.LogInformation("PostgreSQL release {ReleaseName} uninstalled successfully", releaseName);
            }
            else
            {
                _logger.LogInformation("PostgreSQL release {ReleaseName} does not exist, skipping uninstall", releaseName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up PostgreSQL release {ReleaseName}, continuing with cleanup", releaseName);
        }
    }

    private async Task CleanupVaultAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        if (entity.Spec.Components.Vault?.Enabled != true)
        {
            _logger.LogDebug("Vault not enabled for stack {Name}, skipping cleanup", entity.Metadata.Name);
            return;
        }

        var releaseName = $"{entity.Metadata.Name}-vault-operator";
        
        try
        {
            // Clean up Vault-specific resources first
            _logger.LogInformation("Cleaning up Vault resources for stack {Name}", entity.Metadata.Name);
            await _vaultService.CleanupVaultAsync(entity, cancellationToken);

            // Then uninstall the Helm release
            var releaseExists = await _helmService.IsReleaseInstalledAsync(entity.Metadata.NamespaceProperty, releaseName);
            if (releaseExists)
            {
                _logger.LogInformation("Uninstalling Vault release {ReleaseName}", releaseName);
                await _helmService.UninstallReleaseAsync(entity.Metadata.NamespaceProperty, releaseName);
                _logger.LogInformation("Vault release {ReleaseName} uninstalled successfully", releaseName);
            }
            else
            {
                _logger.LogInformation("Vault release {ReleaseName} does not exist, skipping uninstall", releaseName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up Vault release {ReleaseName}, continuing with cleanup", releaseName);
        }
    }
} 