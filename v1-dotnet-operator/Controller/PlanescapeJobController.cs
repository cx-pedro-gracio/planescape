using k8s;
using k8s.Models;
using KubeOps.Operator;
using KubeOps.Operator.Web;
using KubeOps.Abstractions;
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using Microsoft.Extensions.Logging;
using PlanescapeStackOperator.Entities;
using PlanescapeStackOperator.Services;

namespace PlanescapeStackOperator.Controller;

[EntityRbac(typeof(PlanescapeJob), Verbs = RbacVerb.All)]
public class PlanescapeJobController : IEntityController<PlanescapeJob>
{
    private readonly ILogger<PlanescapeJobController> _logger;
    private readonly IKubernetes _kubernetes;
    private readonly IVaultService _vaultService;
    private readonly IStackHealthService _stackHealthService;

    public PlanescapeJobController(
        ILogger<PlanescapeJobController> logger,
        IKubernetes kubernetes,
        IVaultService vaultService,
        IStackHealthService stackHealthService)
    {
        _logger = logger;
        _kubernetes = kubernetes;
        _vaultService = vaultService;
        _stackHealthService = stackHealthService;
    }

    public async Task ReconcileAsync(PlanescapeJob entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reconciling PlanescapeJob {Name} in namespace {Namespace}",
            entity.Metadata.Name, entity.Metadata.NamespaceProperty);

        try
        {
            // Set initial status
            entity.Status.Conditions = new List<JobCondition>
            {
                new()
                {
                    Type = "Reconciling",
                    Status = "True",
                    Reason = "ReconciliationStarted",
                    Message = "Job reconciliation started",
                    LastTransitionTime = DateTime.UtcNow
                }
            };

            // Check if the referenced stack is ready
            var stackName = entity.Metadata.Labels?["stack.planescape.io/name"];
            if (string.IsNullOrEmpty(stackName))
            {
                entity.Status.Conditions = new List<JobCondition>
                {
                    new()
                    {
                        Type = "Error",
                        Status = "True",
                        Reason = "MissingStackReference",
                        Message = "Job must reference a stack via stack.planescape.io/name label",
                        LastTransitionTime = DateTime.UtcNow
                    }
                };
                return;
            }

            var stack = await GetStackAsync(entity.Metadata.NamespaceProperty, stackName);
            if (stack == null)
            {
                entity.Status.Conditions = new List<JobCondition>
                {
                    new()
                    {
                        Type = "Error",
                        Status = "True",
                        Reason = "StackNotFound",
                        Message = $"Referenced stack {stackName} not found",
                        LastTransitionTime = DateTime.UtcNow
                    }
                };
                return;
            }

            // Check if stack is ready
            var stackHealth = await _stackHealthService.CheckStackHealthAsync(stack, cancellationToken);
            if (!stackHealth.All(c => c.Value.Ready))
            {
                entity.Status.Conditions = new List<JobCondition>
                {
                    new()
                    {
                        Type = "Waiting",
                        Status = "True",
                        Reason = "StackNotReady",
                        Message = "Referenced stack is not ready",
                        LastTransitionTime = DateTime.UtcNow
                    }
                };
                return;
            }

            // Create or update service account and RBAC resources
            await CreateOrUpdateServiceAccountAsync(entity, stack);

            // Inject Vault secrets if Vault is enabled
            if (stack.Spec.Components.Vault?.Enabled == true)
            {
                await InjectVaultSecretsAsync(entity, stack, cancellationToken);
            }

            // Create or update the Kubernetes Job/CronJob
            if (string.IsNullOrEmpty(entity.Spec.Schedule) || entity.Spec.Schedule == "@once")
            {
                await CreateOrUpdateJobAsync(entity);
            }
            else
            {
                await CreateOrUpdateCronJobAsync(entity);
            }

            // Set final status
            entity.Status.Conditions = new List<JobCondition>
            {
                new()
                {
                    Type = "Ready",
                    Status = "True",
                    Reason = "JobReady",
                    Message = "Job is ready for execution",
                    LastTransitionTime = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconciling PlanescapeJob {Name}", entity.Metadata.Name);
            entity.Status.Conditions = new List<JobCondition>
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
        }
    }

    public async Task DeletedAsync(PlanescapeJob entity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting PlanescapeJob {Name} in namespace {Namespace}",
            entity.Metadata.Name, entity.Metadata.NamespaceProperty);

        try
        {
            // Delete the Kubernetes Job/CronJob
            if (string.IsNullOrEmpty(entity.Spec.Schedule) || entity.Spec.Schedule == "@once")
            {
                await _kubernetes.BatchV1.DeleteNamespacedJobAsync(
                    entity.Metadata.Name,
                    entity.Metadata.NamespaceProperty);
            }
            else
            {
                await _kubernetes.BatchV1.DeleteNamespacedCronJobAsync(
                    entity.Metadata.Name,
                    entity.Metadata.NamespaceProperty);
            }

            // Delete RBAC resources (they will be automatically deleted due to owner references)
            var serviceAccountName = $"{entity.Metadata.Name}-sa";
            await _kubernetes.CoreV1.DeleteNamespacedServiceAccountAsync(
                serviceAccountName,
                entity.Metadata.NamespaceProperty);

            await _kubernetes.RbacAuthorizationV1.DeleteNamespacedRoleAsync(
                $"{entity.Metadata.Name}-role",
                entity.Metadata.NamespaceProperty);

            await _kubernetes.RbacAuthorizationV1.DeleteNamespacedRoleBindingAsync(
                $"{entity.Metadata.Name}-rolebinding",
                entity.Metadata.NamespaceProperty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting PlanescapeJob {Name}", entity.Metadata.Name);
            throw;
        }
    }

    private async Task<PlanescapeStack?> GetStackAsync(string @namespace, string name)
    {
        try
        {
            var result = await _kubernetes.CustomObjects.GetNamespacedCustomObjectAsync<PlanescapeStack>(
                "planescape.io",
                "v1alpha1",
                @namespace,
                "planescapestacks",
                name);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting PlanescapeStack {Name}", name);
            return null;
        }
    }

    private async Task InjectVaultSecretsAsync(PlanescapeJob job, PlanescapeStack stack, CancellationToken cancellationToken)
    {
        // Add Vault annotations to the pod template
        job.Spec.JobTemplate.Spec.Template.Metadata ??= new V1ObjectMeta();
        job.Spec.JobTemplate.Spec.Template.Metadata.Annotations ??= new Dictionary<string, string>();

        // Add Vault annotations for secret injection
        job.Spec.JobTemplate.Spec.Template.Metadata.Annotations["vault.hashicorp.com/agent-inject"] = "true";
        job.Spec.JobTemplate.Spec.Template.Metadata.Annotations["vault.hashicorp.com/agent-inject-status"] = "update";
        job.Spec.JobTemplate.Spec.Template.Metadata.Annotations["vault.hashicorp.com/role"] = $"{stack.Metadata.Name}-job-role";

        // Add Vault sidecar container
        job.Spec.JobTemplate.Spec.Template.Spec.Containers.Add(new V1Container
        {
            Name = "vault-agent",
            Image = "hashicorp/vault:latest",
            Command = new List<string> { "agent", "-config=/vault/config/config.hcl" },
            VolumeMounts = new List<V1VolumeMount>
            {
                new()
                {
                    Name = "vault-config",
                    MountPath = "/vault/config"
                }
            }
        });

        // Add Vault config volume
        job.Spec.JobTemplate.Spec.Template.Spec.Volumes ??= new List<V1Volume>();
        job.Spec.JobTemplate.Spec.Template.Spec.Volumes.Add(new V1Volume
        {
            Name = "vault-config",
            ConfigMap = new V1ConfigMapVolumeSource
            {
                Name = $"{stack.Metadata.Name}-vault-agent-config"
            }
        });

        // Configure Vault role and policies for the job
        await _vaultService.ConfigureJobVaultAccessAsync(stack, job, cancellationToken);
    }

    private async Task CreateOrUpdateJobAsync(PlanescapeJob entity)
    {
        var job = new V1Job
        {
            Metadata = new V1ObjectMeta
            {
                Name = entity.Metadata.Name,
                NamespaceProperty = entity.Metadata.NamespaceProperty,
                OwnerReferences = new List<V1OwnerReference>
                {
                    new()
                    {
                        ApiVersion = entity.ApiVersion,
                        Kind = entity.Kind,
                        Name = entity.Metadata.Name,
                        Uid = entity.Metadata.Uid,
                        Controller = true,
                        BlockOwnerDeletion = true
                    }
                }
            },
            Spec = new V1JobSpec
            {
                Template = entity.Spec.JobTemplate.Spec.Template,
                BackoffLimit = 4,
                TtlSecondsAfterFinished = 100
            }
        };

        try
        {
            await _kubernetes.BatchV1.CreateNamespacedJobAsync(job, entity.Metadata.NamespaceProperty);
        }
        catch (Exception ex) when (ex.Message.Contains("AlreadyExists"))
        {
            await _kubernetes.BatchV1.ReplaceNamespacedJobAsync(
                job,
                entity.Metadata.Name,
                entity.Metadata.NamespaceProperty);
        }
    }

    private async Task CreateOrUpdateCronJobAsync(PlanescapeJob entity)
    {
        var cronJob = new V1CronJob
        {
            Metadata = new V1ObjectMeta
            {
                Name = entity.Metadata.Name,
                NamespaceProperty = entity.Metadata.NamespaceProperty,
                OwnerReferences = new List<V1OwnerReference>
                {
                    new()
                    {
                        ApiVersion = entity.ApiVersion,
                        Kind = entity.Kind,
                        Name = entity.Metadata.Name,
                        Uid = entity.Metadata.Uid,
                        Controller = true,
                        BlockOwnerDeletion = true
                    }
                }
            },
            Spec = new V1CronJobSpec
            {
                Schedule = entity.Spec.Schedule,
                ConcurrencyPolicy = entity.Spec.ConcurrencyPolicy,
                SuccessfulJobsHistoryLimit = entity.Spec.SuccessfulJobsHistoryLimit,
                FailedJobsHistoryLimit = entity.Spec.FailedJobsHistoryLimit,
                JobTemplate = new V1JobTemplateSpec
                {
                    Spec = new V1JobSpec
                    {
                        Template = entity.Spec.JobTemplate.Spec.Template,
                        BackoffLimit = 4,
                        TtlSecondsAfterFinished = 100
                    }
                }
            }
        };

        try
        {
            await _kubernetes.BatchV1.CreateNamespacedCronJobAsync(cronJob, entity.Metadata.NamespaceProperty);
        }
        catch (Exception ex) when (ex.Message.Contains("AlreadyExists"))
        {
            await _kubernetes.BatchV1.ReplaceNamespacedCronJobAsync(
                cronJob,
                entity.Metadata.Name,
                entity.Metadata.NamespaceProperty);
        }
    }

    private async Task CreateOrUpdateServiceAccountAsync(PlanescapeJob job, PlanescapeStack stack)
    {
        var serviceAccountName = $"{job.Metadata.Name}-sa";
        
        // Create service account
        var serviceAccount = new V1ServiceAccount
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceAccountName,
                NamespaceProperty = job.Metadata.NamespaceProperty,
                OwnerReferences = new List<V1OwnerReference>
                {
                    new()
                    {
                        ApiVersion = job.ApiVersion,
                        Kind = job.Kind,
                        Name = job.Metadata.Name,
                        Uid = job.Metadata.Uid,
                        Controller = true,
                        BlockOwnerDeletion = true
                    }
                }
            }
        };

        try
        {
            await _kubernetes.CoreV1.CreateNamespacedServiceAccountAsync(serviceAccount, job.Metadata.NamespaceProperty);
        }
        catch (Exception ex) when (ex.Message.Contains("AlreadyExists"))
        {
            await _kubernetes.CoreV1.ReplaceNamespacedServiceAccountAsync(
                serviceAccount,
                serviceAccountName,
                job.Metadata.NamespaceProperty);
        }

        // Create role for Vault access
        var role = new V1Role
        {
            Metadata = new V1ObjectMeta
            {
                Name = $"{job.Metadata.Name}-role",
                NamespaceProperty = job.Metadata.NamespaceProperty,
                OwnerReferences = new List<V1OwnerReference>
                {
                    new()
                    {
                        ApiVersion = job.ApiVersion,
                        Kind = job.Kind,
                        Name = job.Metadata.Name,
                        Uid = job.Metadata.Uid,
                        Controller = true,
                        BlockOwnerDeletion = true
                    }
                }
            },
            Rules = new List<V1PolicyRule>
            {
                new()
                {
                    ApiGroups = new[] { "" },
                    Resources = new[] { "secrets" },
                    Verbs = new[] { "get", "list", "watch" }
                }
            }
        };

        try
        {
            await _kubernetes.RbacAuthorizationV1.CreateNamespacedRoleAsync(role, job.Metadata.NamespaceProperty);
        }
        catch (Exception ex) when (ex.Message.Contains("AlreadyExists"))
        {
            await _kubernetes.RbacAuthorizationV1.ReplaceNamespacedRoleAsync(
                role,
                role.Metadata.Name,
                job.Metadata.NamespaceProperty,
                fieldManager: "planescape-operator");
        }

        // Create role binding
        var roleBinding = new V1RoleBinding
        {
            Metadata = new V1ObjectMeta
            {
                Name = $"{job.Metadata.Name}-rolebinding",
                NamespaceProperty = job.Metadata.NamespaceProperty,
                OwnerReferences = new List<V1OwnerReference>
                {
                    new()
                    {
                        ApiVersion = job.ApiVersion,
                        Kind = job.Kind,
                        Name = job.Metadata.Name,
                        Uid = job.Metadata.Uid,
                        Controller = true,
                        BlockOwnerDeletion = true
                    }
                }
            },
            RoleRef = new V1RoleRef
            {
                ApiGroup = "rbac.authorization.k8s.io",
                Kind = "Role",
                Name = role.Metadata.Name
            },
            Subjects = new List<Rbacv1Subject>
            {
                new()
                {
                    Kind = "ServiceAccount",
                    Name = serviceAccountName,
                    NamespaceProperty = job.Metadata.NamespaceProperty
                }
            }
        };

        try
        {
            await _kubernetes.RbacAuthorizationV1.CreateNamespacedRoleBindingAsync(roleBinding, job.Metadata.NamespaceProperty);
        }
        catch (Exception ex) when (ex.Message.Contains("AlreadyExists"))
        {
            await _kubernetes.RbacAuthorizationV1.ReplaceNamespacedRoleBindingAsync(
                roleBinding,
                roleBinding.Metadata.Name,
                job.Metadata.NamespaceProperty,
                fieldManager: "planescape-operator");
        }

        // Update job template to use the service account
        job.Spec.JobTemplate.Spec.Template.Spec.ServiceAccountName = serviceAccountName;
    }
} 