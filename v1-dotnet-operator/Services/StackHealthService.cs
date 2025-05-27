using k8s;
using Microsoft.Extensions.Logging;
using PlanescapeStackOperator.Entities;
using System.Text;
using System.IO;

namespace PlanescapeStackOperator.Services;

public class StackHealthService : IStackHealthService
{
    private readonly ILogger<StackHealthService> _logger;
    private readonly IKubernetes _kubernetes;
    private readonly StringBuilder _execOutput = new();

    public StackHealthService(ILogger<StackHealthService> logger, IKubernetes kubernetes)
    {
        _logger = logger;
        _kubernetes = kubernetes;
    }

    public async Task<Dictionary<string, ComponentStatus>> CheckStackHealthAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking health of stack {StackName}", stack.Metadata.Name);

        var healthStatus = new Dictionary<string, ComponentStatus>();

        if (stack.Spec.Components.Postgresql?.Enabled == true)
        {
            var status = await GetComponentStatusAsync(
                stack.Metadata.NamespaceProperty,
                $"{stack.Metadata.Name}-postgresql",
                "postgresql",
                cancellationToken);
            healthStatus["postgresql"] = status;
        }

        if (stack.Spec.Components.Jenkins?.Enabled == true)
        {
            var status = await GetComponentStatusAsync(
                stack.Metadata.NamespaceProperty,
                $"{stack.Metadata.Name}-jenkins",
                "jenkins",
                cancellationToken);
            healthStatus["jenkins"] = status;
        }

        if (stack.Spec.Components.Vault?.Enabled == true)
        {
            var status = await GetComponentStatusAsync(
                stack.Metadata.NamespaceProperty,
                $"{stack.Metadata.Name}-vault",
                "vault",
                cancellationToken);
            healthStatus["vault"] = status;
        }

        return healthStatus;
    }

    public async Task<bool> IsComponentReadyAsync(string @namespace, string componentName, string componentType, CancellationToken cancellationToken)
    {
        try
        {
            var status = await GetComponentStatusAsync(@namespace, componentName, componentType, cancellationToken);
            return status.Ready;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking component health for {ComponentName}", componentName);
            return false;
        }
    }

    public async Task<ComponentStatus> GetComponentStatusAsync(string @namespace, string componentName, string componentType, CancellationToken cancellationToken)
    {
        try
        {
            switch (componentType.ToLower())
            {
                case "postgresql":
                    return await CheckPostgresqlHealthAsync(@namespace, componentName, cancellationToken);
                case "jenkins":
                    return await CheckJenkinsHealthAsync(@namespace, componentName, cancellationToken);
                case "vault":
                    return await CheckVaultHealthAsync(@namespace, componentName, cancellationToken);
                default:
                    throw new ArgumentException($"Unknown component type: {componentType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting component status for {ComponentName}", componentName);
            return new ComponentStatus
            {
                Ready = false,
                Message = $"Error checking health: {ex.Message}",
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    private async Task<ComponentStatus> CheckPostgresqlHealthAsync(string @namespace, string componentName, CancellationToken cancellationToken)
    {
        // Check if the PostgreSQL pod is running
        var pod = await _kubernetes.CoreV1.ReadNamespacedPodAsync(
            $"{componentName}-0",
            @namespace);

        if (pod.Status.Phase != "Running" || pod.Status.ContainerStatuses?.All(c => c.Ready) != true)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = "PostgreSQL pod is not ready",
                LastUpdated = DateTime.UtcNow
            };
        }

        // Check if the service is available
        var service = await _kubernetes.CoreV1.ReadNamespacedServiceAsync(
            componentName,
            @namespace);

        if (service == null)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = "PostgreSQL service not found",
                LastUpdated = DateTime.UtcNow
            };
        }

        // Check if we can connect to PostgreSQL
        try
        {
            _execOutput.Clear();
            await _kubernetes.NamespacedPodExecAsync(
                pod.Metadata.Name,
                @namespace,
                pod.Spec.Containers[0].Name,
                new[] { "pg_isready", "-h", "localhost" },
                true,
                async (stdIn, stdOut, stdErr) =>
                {
                    using var reader = new StreamReader(stdOut);
                    var output = await reader.ReadToEndAsync();
                    _execOutput.AppendLine(output);

                    using var errorReader = new StreamReader(stdErr);
                    var error = await errorReader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(error))
                    {
                        _execOutput.AppendLine(error);
                    }
                },
                cancellationToken);

            var output = _execOutput.ToString();
            if (!output.Contains("accepting connections"))
            {
                return new ComponentStatus
                {
                    Ready = false,
                    Message = "PostgreSQL is not accepting connections",
                    LastUpdated = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = $"Error checking PostgreSQL connection: {ex.Message}",
                LastUpdated = DateTime.UtcNow
            };
        }

        return new ComponentStatus
        {
            Ready = true,
            Message = "PostgreSQL is healthy",
            LastUpdated = DateTime.UtcNow
        };
    }

    private async Task<ComponentStatus> CheckJenkinsHealthAsync(string @namespace, string componentName, CancellationToken cancellationToken)
    {
        // Check if the Jenkins pod is running
        var pod = await _kubernetes.CoreV1.ReadNamespacedPodAsync(
            $"{componentName}-0",
            @namespace);

        if (pod.Status.Phase != "Running" || pod.Status.ContainerStatuses?.All(c => c.Ready) != true)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = "Jenkins pod is not ready",
                LastUpdated = DateTime.UtcNow
            };
        }

        // Check if the service is available
        var service = await _kubernetes.CoreV1.ReadNamespacedServiceAsync(
            componentName,
            @namespace);

        if (service == null)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = "Jenkins service not found",
                LastUpdated = DateTime.UtcNow
            };
        }

        // Check if Jenkins is responding
        try
        {
            _execOutput.Clear();
            await _kubernetes.NamespacedPodExecAsync(
                pod.Metadata.Name,
                @namespace,
                pod.Spec.Containers[0].Name,
                new[] { "curl", "-s", "-f", "http://localhost:8080/api/json" },
                true,
                async (stdIn, stdOut, stdErr) =>
                {
                    using var reader = new StreamReader(stdOut);
                    var output = await reader.ReadToEndAsync();
                    _execOutput.AppendLine(output);

                    using var errorReader = new StreamReader(stdErr);
                    var error = await errorReader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(error))
                    {
                        _execOutput.AppendLine(error);
                    }
                },
                cancellationToken);

            var output = _execOutput.ToString();
            if (string.IsNullOrEmpty(output))
            {
                return new ComponentStatus
                {
                    Ready = false,
                    Message = "Jenkins API is not responding",
                    LastUpdated = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = $"Error checking Jenkins API: {ex.Message}",
                LastUpdated = DateTime.UtcNow
            };
        }

        return new ComponentStatus
        {
            Ready = true,
            Message = "Jenkins is healthy",
            LastUpdated = DateTime.UtcNow
        };
    }

    private async Task<ComponentStatus> CheckVaultHealthAsync(string @namespace, string componentName, CancellationToken cancellationToken)
    {
        // Check if the Vault pod is running
        var pod = await _kubernetes.CoreV1.ReadNamespacedPodAsync(
            $"{componentName}-0",
            @namespace);

        if (pod.Status.Phase != "Running" || pod.Status.ContainerStatuses?.All(c => c.Ready) != true)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = "Vault pod is not ready",
                LastUpdated = DateTime.UtcNow
            };
        }

        // Check if the service is available
        var service = await _kubernetes.CoreV1.ReadNamespacedServiceAsync(
            componentName,
            @namespace);

        if (service == null)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = "Vault service not found",
                LastUpdated = DateTime.UtcNow
            };
        }

        // Check if Vault is sealed
        try
        {
            _execOutput.Clear();
            await _kubernetes.NamespacedPodExecAsync(
                pod.Metadata.Name,
                @namespace,
                pod.Spec.Containers[0].Name,
                new[] { "vault", "status", "-format=json" },
                true,
                async (stdIn, stdOut, stdErr) =>
                {
                    using var reader = new StreamReader(stdOut);
                    var output = await reader.ReadToEndAsync();
                    _execOutput.AppendLine(output);

                    using var errorReader = new StreamReader(stdErr);
                    var error = await errorReader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(error))
                    {
                        _execOutput.AppendLine(error);
                    }
                },
                cancellationToken);

            var output = _execOutput.ToString();
            if (output.Contains("\"sealed\": true"))
            {
                return new ComponentStatus
                {
                    Ready = false,
                    Message = "Vault is sealed",
                    LastUpdated = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            return new ComponentStatus
            {
                Ready = false,
                Message = $"Error checking Vault status: {ex.Message}",
                LastUpdated = DateTime.UtcNow
            };
        }

        return new ComponentStatus
        {
            Ready = true,
            Message = "Vault is healthy",
            LastUpdated = DateTime.UtcNow
        };
    }
} 