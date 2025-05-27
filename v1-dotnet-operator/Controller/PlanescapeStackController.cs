using k8s;
using k8s.Models;
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using Microsoft.Extensions.Logging;
using PlanescapeStackOperator.Entities;
using PlanescapeStackOperator.Services;
using PlanescapeStackOperator.Finalizer;
using System.Text;
using System.Text.Json;

namespace PlanescapeStackOperator.Controller;

[EntityRbac(typeof(PlanescapeStack), Verbs = RbacVerb.All)]
public class PlanescapeStackController : IEntityController<PlanescapeStack>
{
    private readonly ILogger<PlanescapeStackController> _logger;
    private readonly IKubernetes _kubernetes;
    private readonly IHelmService _helmService;
    private readonly IVaultService _vaultService;
    private readonly IStackHealthService _stackHealthService;
    private readonly IPlanescapeStackFinalizer _finalizer;

    private const string FinalizerName = "planescape.io/stack-finalizer";

    public PlanescapeStackController(
        ILogger<PlanescapeStackController> logger,
        IKubernetes kubernetes,
        IHelmService helmService,
        IVaultService vaultService,
        IStackHealthService stackHealthService,
        IPlanescapeStackFinalizer finalizer)
    {
        _logger = logger;
        _kubernetes = kubernetes;
        _helmService = helmService;
        _vaultService = vaultService;
        _stackHealthService = stackHealthService;
        _finalizer = finalizer;
    }

    public async Task ReconcileAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting reconciliation of PlanescapeStack {Name} in namespace {Namespace}",
            entity.Metadata.Name, entity.Metadata.NamespaceProperty);

        // Check if the resource is being deleted
        if (entity.Metadata.DeletionTimestamp.HasValue)
        {
            _logger.LogInformation("PlanescapeStack {Name} is being deleted (deletionTimestamp: {Timestamp}), handling finalizer cleanup",
                entity.Metadata.Name, entity.Metadata.DeletionTimestamp);
            
            // Check if our finalizer is present
            if (entity.Metadata.Finalizers?.Contains("planescape.io/stack-finalizer") == true)
            {
                try
                {
                    _logger.LogInformation("Finalizer 'planescape.io/stack-finalizer' found on PlanescapeStack {Name}, proceeding with cleanup",
                        entity.Metadata.Name);
                    
                    // Call the finalizer logic
                    await _finalizer.FinalizeAsync(entity, cancellationToken);
                    
                    // Remove the finalizer
                    await RemoveFinalizerAsync(entity, cancellationToken);
                    _logger.LogInformation("Finalizer removed for PlanescapeStack {Name}, resource will be deleted by Kubernetes",
                        entity.Metadata.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during finalizer cleanup for PlanescapeStack {Name}. The resource will remain until cleanup succeeds",
                        entity.Metadata.Name);
                    throw;
                }
            }
            else
            {
                _logger.LogInformation("No finalizer found on PlanescapeStack {Name}, skipping cleanup",
                    entity.Metadata.Name);
            }
            return;
        }

        try
        {
            // Add finalizer if not present
            await EnsureFinalizerAsync(entity, cancellationToken);

            // Set initial status
            _logger.LogInformation("Setting initial reconciliation status for PlanescapeStack {Name}",
                entity.Metadata.Name);
            entity.Status.Conditions = new List<StackCondition>
            {
                new StackCondition
                {
                    Type = "Reconciling",
                    Status = "True",
                    Reason = "ReconciliationStarted",
                    Message = "Stack reconciliation started",
                    LastTransitionTime = DateTime.UtcNow
                }
            };

            // Deploy and initialize Vault first (basic setup)
            if (entity.Spec.Components.Vault?.Enabled == true)
            {
                _logger.LogInformation("Vault component enabled for PlanescapeStack {Name}, deploying Vault operator",
                    entity.Metadata.Name);
                await DeployVaultOperatorAsync(entity);
                
                _logger.LogInformation("Initializing Vault for PlanescapeStack {Name} (this may take a few minutes)",
                    entity.Metadata.Name);
                await _vaultService.InitializeVaultAsync(entity, cancellationToken);
                _logger.LogInformation("Vault initialization completed for PlanescapeStack {Name}",
                    entity.Metadata.Name);
            }
            else
            {
                _logger.LogInformation("Vault component disabled for PlanescapeStack {Name}, skipping Vault deployment",
                    entity.Metadata.Name);
            }

            // Deploy PostgreSQL if enabled
            if (entity.Spec.Components.Postgresql?.Enabled == true)
            {
                _logger.LogInformation("PostgreSQL component enabled for PlanescapeStack {Name}, deploying PostgreSQL operator",
                    entity.Metadata.Name);
                await DeployPostgresqlOperatorAsync(entity);
                
                _logger.LogInformation("Waiting for PostgreSQL to be ready for PlanescapeStack {Name} (this may take a few minutes)",
                    entity.Metadata.Name);
                await WaitForPostgresqlReadyAsync(entity, cancellationToken);
                _logger.LogInformation("PostgreSQL is ready for PlanescapeStack {Name}",
                    entity.Metadata.Name);
                
                _logger.LogInformation("Configuring PostgreSQL secrets for PlanescapeStack {Name}",
                    entity.Metadata.Name);
                await ConfigurePostgresqlSecretsAsync(entity, cancellationToken);
                
                // Configure Vault's PostgreSQL database connection
                if (entity.Spec.Components.Vault?.Enabled == true)
                {
                    _logger.LogInformation("Configuring PostgreSQL database connection in Vault for PlanescapeStack {Name}",
                        entity.Metadata.Name);
                    await _vaultService.ConfigurePostgresqlDatabaseAsync(entity, cancellationToken);
                }
            }
            else
            {
                _logger.LogInformation("PostgreSQL component disabled for PlanescapeStack {Name}, skipping PostgreSQL deployment",
                    entity.Metadata.Name);
            }

            // Deploy Jenkins if enabled
            if (entity.Spec.Components.Jenkins?.Enabled == true)
            {
                _logger.LogInformation("Jenkins component enabled for PlanescapeStack {Name}, deploying Jenkins operator",
                    entity.Metadata.Name);
                await DeployJenkinsOperatorAsync(entity);
                
                _logger.LogInformation("Waiting for Jenkins to be ready for PlanescapeStack {Name} (this may take a few minutes)",
                    entity.Metadata.Name);
                await WaitForJenkinsReadyAsync(entity, cancellationToken);
                _logger.LogInformation("Jenkins is ready for PlanescapeStack {Name}",
                    entity.Metadata.Name);
                
                _logger.LogInformation("Configuring Jenkins secrets for PlanescapeStack {Name}",
                    entity.Metadata.Name);
                await ConfigureJenkinsSecretsAsync(entity, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Jenkins component disabled for PlanescapeStack {Name}, skipping Jenkins deployment",
                    entity.Metadata.Name);
            }
            
            // Final Vault configuration (Jenkins secrets, etc.)
            if (entity.Spec.Components.Vault?.Enabled == true)
            {
                _logger.LogInformation("Performing final Vault configuration for PlanescapeStack {Name}",
                    entity.Metadata.Name);
                await _vaultService.ConfigureVaultAsync(entity, cancellationToken);
            }

            // Check health of all components
            _logger.LogInformation("Checking health of all components for PlanescapeStack {Name}",
                entity.Metadata.Name);
            var healthStatus = await _stackHealthService.CheckStackHealthAsync(entity, cancellationToken);
            entity.Status.Components = healthStatus;

            // Update final status based on component health
            var allComponentsReady = healthStatus.All(c => c.Value.Ready);
            entity.Status.Conditions = new List<StackCondition>
            {
                new()
                {
                    Type = allComponentsReady ? "Ready" : "Degraded",
                    Status = "True",
                    Reason = allComponentsReady ? "StackReady" : "ComponentsNotReady",
                    Message = allComponentsReady 
                        ? "All stack components are ready" 
                        : "Some stack components are not ready",
                    LastTransitionTime = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Successfully completed reconciliation of PlanescapeStack {Name}. Status: {Status}",
                entity.Metadata.Name, allComponentsReady ? "Ready" : "Degraded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconciling PlanescapeStack {Name}. The operator will retry reconciliation",
                entity.Metadata.Name);
            entity.Status.Conditions = new List<StackCondition>
            {
                new()
                {
                    Type = "Error",
                    Status = "True",
                    Reason = "ReconciliationError",
                    Message = $"Error during reconciliation: {ex.Message}",
                    LastTransitionTime = DateTime.UtcNow
                }
            };
            throw; // Re-throw to trigger retry
        }
    }

    public Task DeletedAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("PlanescapeStack {Name} deleted, cleanup handled by finalizer",
            entity.Metadata.Name);
        
        // The actual cleanup is handled by the PlanescapeStackFinalizer
        // This method is called after the finalizer has completed
        return Task.CompletedTask;
    }

    private async Task EnsureFinalizerAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        if (entity.Metadata.Finalizers?.Contains(FinalizerName) != true)
            {
            _logger.LogInformation("Adding finalizer '{FinalizerName}' to PlanescapeStack {Name} to ensure proper cleanup on deletion",
                FinalizerName, entity.Metadata.Name);
            
            entity.Metadata.Finalizers ??= new List<string>();
            entity.Metadata.Finalizers.Add(FinalizerName);
            
            // Update the resource with the finalizer
            await _kubernetes.CustomObjects.PatchNamespacedCustomObjectAsync(
                new V1Patch(JsonSerializer.Serialize(new
                {
                    metadata = new
                    {
                        finalizers = entity.Metadata.Finalizers
                    }
                }), V1Patch.PatchType.MergePatch),
                "planescape.io",
                "v1alpha1",
                entity.Metadata.NamespaceProperty,
                "planescapestacks",
                entity.Metadata.Name,
                cancellationToken: cancellationToken);
                
            _logger.LogInformation("Finalizer '{FinalizerName}' added to PlanescapeStack {Name}",
                FinalizerName, entity.Metadata.Name);
        }
        else
        {
            _logger.LogDebug("Finalizer '{FinalizerName}' already present on PlanescapeStack {Name}",
                FinalizerName, entity.Metadata.Name);
        }
    }

    private async Task DeployVaultOperatorAsync(PlanescapeStack stack)
    {
        var releaseName = $"{stack.Metadata.Name}-vault-operator";
        _logger.LogInformation("Deploying Vault operator for PlanescapeStack {Name} using Helm release {ReleaseName}",
            stack.Metadata.Name, releaseName);
        
        var values = new Dictionary<string, object>
        {
            ["server"] = new
            {
                ha = new
                {
                    enabled = stack.Spec.Components.Vault?.Server?.HA?.Enabled,
                    replicas = stack.Spec.Components.Vault?.Server?.HA?.Replicas
                },
                resources = new
                {
                    limits = new Dictionary<string, string>
                    {
                        ["cpu"] = stack.Spec.Components.Vault?.Server?.Resources?.Limits?.Cpu ?? "500m",
                        ["memory"] = stack.Spec.Components.Vault?.Server?.Resources?.Limits?.Memory ?? "512Mi"
                    },
                    requests = new Dictionary<string, string>
                    {
                        ["cpu"] = stack.Spec.Components.Vault?.Server?.Resources?.Requests?.Cpu ?? "100m",
                        ["memory"] = stack.Spec.Components.Vault?.Server?.Resources?.Requests?.Memory ?? "256Mi"
                    }
                },
                service = new
                {
                    type = stack.Spec.Components.Vault?.Server?.Service?.Type ?? "ClusterIP",
                    port = stack.Spec.Components.Vault?.Server?.Service?.Port ?? 8200
                },
                dataStorage = new
                {
                    size = stack.Spec.Components.Vault?.Server?.DataStorage?.Size ?? "10Gi"
                }
            }
        };

        _logger.LogInformation("Installing Vault operator with configuration: HA={HAEnabled}, Replicas={Replicas}, ServiceType={ServiceType}, StorageSize={StorageSize}",
            stack.Spec.Components.Vault?.Server?.HA?.Enabled ?? false,
            stack.Spec.Components.Vault?.Server?.HA?.Replicas ?? 1,
            stack.Spec.Components.Vault?.Server?.Service?.Type ?? "ClusterIP",
            stack.Spec.Components.Vault?.Server?.DataStorage?.Size ?? "10Gi");

        await _helmService.InstallOrUpgradeReleaseAsync(
            stack.Metadata.NamespaceProperty,
            releaseName,
            "hashicorp/vault",
            values);
            
        _logger.LogInformation("Vault operator deployment completed for PlanescapeStack {Name}",
            stack.Metadata.Name);
    }

    private async Task DeployPostgresqlOperatorAsync(PlanescapeStack stack)
    {
        var releaseName = $"{stack.Metadata.Name}-postgresql";
        _logger.LogInformation("Deploying PostgreSQL for PlanescapeStack {Name} using Helm release {ReleaseName}",
            stack.Metadata.Name, releaseName);
        
        // Try to get existing password from Vault, or generate a new one if it doesn't exist
        string postgresPassword;
        try
        {
            _logger.LogInformation("Attempting to retrieve existing PostgreSQL password from Vault for PlanescapeStack {Name}",
                stack.Metadata.Name);
            postgresPassword = await _vaultService.GetSecretAsync(
                stack.Metadata.NamespaceProperty,
                $"{stack.Metadata.Name}/postgresql",
                "password",
                CancellationToken.None);
            _logger.LogInformation("Successfully retrieved existing PostgreSQL password from Vault for PlanescapeStack {Name}",
                stack.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("No existing PostgreSQL password found in Vault for PlanescapeStack {Name}, generating new one: {Error}",
                stack.Metadata.Name, ex.Message);
            // Generate a deterministic password for PostgreSQL based on stack name and UID
            var passwordSeed = $"{stack.Metadata.Name}-{stack.Metadata.Uid}-postgresql";
            postgresPassword = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(passwordSeed)))[..32];
        }
        
        var values = new Dictionary<string, object>
        {
            ["auth"] = new
            {
                postgresPassword = postgresPassword,
                database = stack.Spec.Components.Postgresql?.Auth?.Database ?? "postgres"
            },
            ["primary"] = new
            {
                persistence = new
                {
                    size = stack.Spec.Components.Postgresql?.Primary?.Persistence?.Size ?? "8Gi"
                },
                resources = new
                {
                    limits = new Dictionary<string, string>
                    {
                        ["cpu"] = stack.Spec.Components.Postgresql?.Primary?.Resources?.Limits?.Cpu ?? "500m",
                        ["memory"] = stack.Spec.Components.Postgresql?.Primary?.Resources?.Limits?.Memory ?? "512Mi"
                    },
                    requests = new Dictionary<string, string>
                    {
                        ["cpu"] = stack.Spec.Components.Postgresql?.Primary?.Resources?.Requests?.Cpu ?? "100m",
                        ["memory"] = stack.Spec.Components.Postgresql?.Primary?.Resources?.Requests?.Memory ?? "256Mi"
                    }
                },
                service = new
                {
                    type = stack.Spec.Components.Postgresql?.Primary?.Service?.Type ?? "ClusterIP",
                    ports = new
                    {
                        postgresql = stack.Spec.Components.Postgresql?.Primary?.Service?.Port ?? 5432
                    }
                }
            }
        };

        _logger.LogInformation("Installing PostgreSQL with configuration: Database={Database}, ServiceType={ServiceType}, StorageSize={StorageSize}",
            stack.Spec.Components.Postgresql?.Auth?.Database ?? "postgres",
            stack.Spec.Components.Postgresql?.Primary?.Service?.Type ?? "ClusterIP",
            stack.Spec.Components.Postgresql?.Primary?.Persistence?.Size ?? "8Gi");

        await _helmService.InstallOrUpgradeReleaseAsync(
            stack.Metadata.NamespaceProperty,
            releaseName,
            "bitnami/postgresql",
            values);
            
        _logger.LogInformation("PostgreSQL deployment completed for PlanescapeStack {Name}",
            stack.Metadata.Name);
    }

    private async Task DeployJenkinsOperatorAsync(PlanescapeStack stack)
    {
        var releaseName = $"{stack.Metadata.Name}-jenkins";
        _logger.LogInformation("Deploying Jenkins for PlanescapeStack {Name} using Helm release {ReleaseName}",
            stack.Metadata.Name, releaseName);
        
        // Try to get existing password from Vault, or generate a new one if it doesn't exist
        string jenkinsAdminPassword;
        try
        {
            _logger.LogInformation("Attempting to retrieve existing Jenkins password from Vault for PlanescapeStack {Name}",
                stack.Metadata.Name);
            jenkinsAdminPassword = await _vaultService.GetSecretAsync(
                stack.Metadata.NamespaceProperty,
                $"{stack.Metadata.Name}/jenkins",
                "admin-password",
                CancellationToken.None);
            _logger.LogInformation("Successfully retrieved existing Jenkins password from Vault for PlanescapeStack {Name}",
                stack.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("No existing Jenkins password found in Vault for PlanescapeStack {Name}, generating new one: {Error}",
                stack.Metadata.Name, ex.Message);
            // Generate a deterministic password for Jenkins based on stack name and UID
            var passwordSeed = $"{stack.Metadata.Name}-{stack.Metadata.Uid}-jenkins";
            jenkinsAdminPassword = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(passwordSeed)))[..32];
        }
        
        var values = new Dictionary<string, object>
        {
            ["controller"] = new
            {
                adminPassword = jenkinsAdminPassword,
                serviceType = stack.Spec.Components.Jenkins?.Controller?.ServiceType ?? "ClusterIP",
                resources = new
                {
                    limits = new Dictionary<string, string>
                    {
                        ["cpu"] = stack.Spec.Components.Jenkins?.Controller?.Resources?.Limits?.Cpu ?? "500m",
                        ["memory"] = stack.Spec.Components.Jenkins?.Controller?.Resources?.Limits?.Memory ?? "512Mi"
                    },
                    requests = new Dictionary<string, string>
                    {
                        ["cpu"] = stack.Spec.Components.Jenkins?.Controller?.Resources?.Requests?.Cpu ?? "100m",
                        ["memory"] = stack.Spec.Components.Jenkins?.Controller?.Resources?.Requests?.Memory ?? "256Mi"
                    }
                },
                persistence = new
                {
                    enabled = true,
                    size = stack.Spec.Components.Jenkins?.Controller?.Persistence?.Size ?? "8Gi"
                }
            }
        };

        var plugins = stack.Spec.Components.Jenkins?.Controller?.InstallPlugins?.ToList() ?? new List<string>();
        _logger.LogInformation("Installing Jenkins with configuration: ServiceType={ServiceType}, StorageSize={StorageSize}, Plugins={Plugins}",
            stack.Spec.Components.Jenkins?.Controller?.ServiceType ?? "ClusterIP",
            stack.Spec.Components.Jenkins?.Controller?.Persistence?.Size ?? "8Gi",
            string.Join(", ", plugins));

        await _helmService.InstallOrUpgradeReleaseAsync(
            stack.Metadata.NamespaceProperty,
            releaseName,
            "bitnami/jenkins",
            values);
            
        _logger.LogInformation("Jenkins deployment completed for PlanescapeStack {Name}",
            stack.Metadata.Name);
    }

    private async Task ConfigurePostgresqlSecretsAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        // Check if password already exists in Vault
        try
        {
            await _vaultService.GetSecretAsync(
                stack.Metadata.NamespaceProperty,
                $"{stack.Metadata.Name}/postgresql",
                "password",
                cancellationToken);
            _logger.LogInformation("PostgreSQL password already exists in Vault, skipping storage");
        }
        catch (Exception)
        {
            // Password doesn't exist, generate and store it
            var passwordSeed = $"{stack.Metadata.Name}-{stack.Metadata.Uid}-postgresql";
            var postgresPassword = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(passwordSeed)))[..32];
        
        await _vaultService.StoreSecretAsync(
            stack.Metadata.NamespaceProperty,
            $"{stack.Metadata.Name}/postgresql",
            "password",
            postgresPassword,
            cancellationToken);
            
        _logger.LogInformation("PostgreSQL credentials stored in Vault");
        }
    }

    private async Task ConfigureJenkinsSecretsAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        // Check if password already exists in Vault
        try
        {
            await _vaultService.GetSecretAsync(
                stack.Metadata.NamespaceProperty,
                $"{stack.Metadata.Name}/jenkins",
                "admin-password",
                cancellationToken);
            _logger.LogInformation("Jenkins password already exists in Vault, skipping storage");
        }
        catch (Exception)
        {
            // Password doesn't exist, generate and store it
            var passwordSeed = $"{stack.Metadata.Name}-{stack.Metadata.Uid}-jenkins";
            var adminPassword = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(passwordSeed)))[..32];
        
        await _vaultService.StoreSecretAsync(
            stack.Metadata.NamespaceProperty,
            $"{stack.Metadata.Name}/jenkins",
            "admin-password",
            adminPassword,
            cancellationToken);
            
        _logger.LogInformation("Jenkins credentials stored in Vault");
        }
    }

    private async Task WaitForPostgresqlReadyAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Waiting for PostgreSQL to be ready for PlanescapeStack {Name} (this may take a few minutes)...",
            stack.Metadata.Name);
        var podName = $"{stack.Metadata.Name}-postgresql-0";
        
        for (int i = 0; i < 60; i++) // Wait up to 5 minutes
        {
            try
            {
                var pod = await _kubernetes.CoreV1.ReadNamespacedPodAsync(
                    podName, 
                    stack.Metadata.NamespaceProperty, 
                    cancellationToken: cancellationToken);
                
                if (pod.Status.Phase == "Running" && 
                    pod.Status.ContainerStatuses?.All(c => c.Ready) == true)
                {
                    _logger.LogInformation("PostgreSQL pod {PodName} is ready for PlanescapeStack {Name}",
                        podName, stack.Metadata.Name);
                    return;
                }
                
                _logger.LogDebug("PostgreSQL pod {PodName} status: Phase={Phase}, Ready={Ready}",
                    podName, pod.Status.Phase, pod.Status.ContainerStatuses?.All(c => c.Ready));
            }
            catch (Exception ex) when (ex.Message.Contains("NotFound"))
            {
                _logger.LogDebug("PostgreSQL pod {PodName} not found yet, continuing to wait",
                    podName);
            }
            
            await Task.Delay(5000, cancellationToken); // Wait 5 seconds
        }
        
        throw new Exception($"PostgreSQL pod {podName} did not become ready within the timeout period");
    }

    private async Task WaitForJenkinsReadyAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Waiting for Jenkins to be ready for PlanescapeStack {Name} (this may take a few minutes)...",
            stack.Metadata.Name);
        var podName = $"{stack.Metadata.Name}-jenkins-0";
        
        for (int i = 0; i < 60; i++) // Wait up to 5 minutes
        {
            try
            {
                var pod = await _kubernetes.CoreV1.ReadNamespacedPodAsync(
                    podName, 
                    stack.Metadata.NamespaceProperty, 
                    cancellationToken: cancellationToken);
                
                if (pod.Status.Phase == "Running" && 
                    pod.Status.ContainerStatuses?.All(c => c.Ready) == true)
                {
                    _logger.LogInformation("Jenkins pod {PodName} is ready for PlanescapeStack {Name}",
                        podName, stack.Metadata.Name);
                    return;
                }
                
                _logger.LogDebug("Jenkins pod {PodName} status: Phase={Phase}, Ready={Ready}",
                    podName, pod.Status.Phase, pod.Status.ContainerStatuses?.All(c => c.Ready));
            }
            catch (Exception ex) when (ex.Message.Contains("NotFound"))
            {
                _logger.LogDebug("Jenkins pod {PodName} not found yet, continuing to wait",
                    podName);
            }
            
            await Task.Delay(5000, cancellationToken); // Wait 5 seconds
        }
        
        throw new Exception($"Jenkins pod {podName} did not become ready within the timeout period");
    }

    private async Task RemoveFinalizerAsync(PlanescapeStack entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Removing finalizer '{FinalizerName}' from PlanescapeStack {Name} to allow Kubernetes to delete the resource",
            FinalizerName, entity.Metadata.Name);
        
        entity.Metadata.Finalizers?.Remove(FinalizerName);
        
        // Update the resource with the finalizer
        await _kubernetes.CustomObjects.PatchNamespacedCustomObjectAsync(
            new V1Patch(JsonSerializer.Serialize(new
            {
                metadata = new
                {
                    finalizers = entity.Metadata.Finalizers
                }
            }), V1Patch.PatchType.MergePatch),
            "planescape.io",
            "v1alpha1",
            entity.Metadata.NamespaceProperty,
            "planescapestacks",
            entity.Metadata.Name,
            cancellationToken: cancellationToken);
            
        _logger.LogInformation("Finalizer '{FinalizerName}' removed from PlanescapeStack {Name}",
            FinalizerName, entity.Metadata.Name);
    }
} 