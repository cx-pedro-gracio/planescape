using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PlanescapeStackOperator.Services;

public class HelmService : IHelmService
{
    private readonly ILogger<HelmService> _logger;

    public HelmService(ILogger<HelmService> logger)
    {
        _logger = logger;
    }

    public async Task InstallOrUpgradeReleaseAsync(string @namespace, string releaseName, string chartName, Dictionary<string, object> values)
    {
        _logger.LogInformation("Installing/upgrading Helm release {ReleaseName} in namespace {Namespace}",
            releaseName, @namespace);

        // Ensure required repositories are added
        await EnsureRepositoriesAsync(chartName);

        // Check if release exists and if values have changed
        var releaseExists = await IsReleaseInstalledAsync(@namespace, releaseName);
        if (releaseExists)
        {
            var hasChanges = await HasValuesChangedAsync(@namespace, releaseName, values);
            if (!hasChanges)
            {
                _logger.LogInformation("Helm release {ReleaseName} is already up-to-date, skipping upgrade", releaseName);
                return;
            }
            _logger.LogInformation("Helm release {ReleaseName} values have changed, proceeding with upgrade", releaseName);
        }
        else
        {
            _logger.LogInformation("Helm release {ReleaseName} does not exist, proceeding with installation", releaseName);
        }

        var valuesFile = Path.GetTempFileName();
        try
        {
            // Write values to a temporary file
            await File.WriteAllTextAsync(valuesFile, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));

            // Run helm upgrade --install
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "helm",
                    Arguments = $"upgrade --install {releaseName} {chartName} --namespace {@namespace} --values {valuesFile} --wait --timeout 10m",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogInformation("Helm command output: {Output}", output);
            if (!string.IsNullOrEmpty(error))
            {
                // Check if it's just a warning about no changes
                if (error.Contains("has no deployed releases") || error.Contains("nothing to upgrade"))
                {
                    _logger.LogInformation("Helm release {ReleaseName} has no changes to apply", releaseName);
                }
                else
            {
                _logger.LogWarning("Helm command error: {Error}", error);
                }
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"Helm command failed: {error}");
            }

            _logger.LogInformation("Helm release {ReleaseName} installed/upgraded successfully", releaseName);
        }
        finally
        {
            if (File.Exists(valuesFile))
            {
                File.Delete(valuesFile);
            }
        }
    }

    private async Task EnsureRepositoriesAsync(string chartName)
    {
        var repositories = new Dictionary<string, string>
        {
            ["hashicorp"] = "https://helm.releases.hashicorp.com",
            ["bitnami"] = "https://charts.bitnami.com/bitnami"
        };

        string? repoName = null;
        if (chartName.StartsWith("hashicorp/"))
            repoName = "hashicorp";
        else if (chartName.StartsWith("bitnami/"))
            repoName = "bitnami";

        if (repoName != null && repositories.TryGetValue(repoName, out var repoUrl))
        {
            await AddRepositoryAsync(repoName, repoUrl);
        }
    }

    private async Task AddRepositoryAsync(string repoName, string repoUrl)
    {
        _logger.LogInformation("Adding Helm repository {RepoName} ({RepoUrl})", repoName, repoUrl);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "helm",
                Arguments = $"repo add {repoName} {repoUrl}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        // Update repositories
        var updateProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "helm",
                Arguments = "repo update",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        updateProcess.Start();
        await updateProcess.WaitForExitAsync();

        _logger.LogInformation("Helm repository {RepoName} added and updated", repoName);
    }

    public async Task UninstallReleaseAsync(string @namespace, string releaseName)
    {
        _logger.LogInformation("Uninstalling Helm release {ReleaseName} from namespace {Namespace}",
            releaseName, @namespace);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "helm",
                Arguments = $"uninstall {releaseName} --namespace {@namespace} --wait --timeout 5m",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Helm uninstall failed: {error}");
        }

        _logger.LogInformation("Helm release {ReleaseName} uninstalled successfully", releaseName);
    }

    public async Task<bool> IsReleaseInstalledAsync(string @namespace, string releaseName)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "helm",
                Arguments = $"status {releaseName} --namespace {@namespace}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        return process.ExitCode == 0;
    }

    public async Task<Dictionary<string, object>> GetReleaseValuesAsync(string @namespace, string releaseName)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "helm",
                Arguments = $"get values {releaseName} --namespace {@namespace} --output json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Failed to get Helm release values: {error}");
        }

        return JsonSerializer.Deserialize<Dictionary<string, object>>(output) ?? new Dictionary<string, object>();
    }

    private async Task<bool> HasValuesChangedAsync(string @namespace, string releaseName, Dictionary<string, object> newValues)
    {
        try
        {
            var currentValues = await GetReleaseValuesAsync(@namespace, releaseName);
            
            // Serialize both to JSON for comparison
            var currentJson = JsonSerializer.Serialize(currentValues, new JsonSerializerOptions { WriteIndented = true });
            var newJson = JsonSerializer.Serialize(newValues, new JsonSerializerOptions { WriteIndented = true });
            
            var hasChanged = !string.Equals(currentJson, newJson, StringComparison.Ordinal);
            
            if (hasChanged)
            {
                _logger.LogDebug("Values comparison for {ReleaseName}:\nCurrent: {Current}\nNew: {New}", 
                    releaseName, currentJson, newJson);
            }
            
            return hasChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not compare values for release {ReleaseName}, assuming changes exist", releaseName);
            return true; // Assume changes exist if we can't compare
        }
    }
} 