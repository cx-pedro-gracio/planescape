namespace PlanescapeStackOperator.Services;

public interface IHelmService
{
    Task InstallOrUpgradeReleaseAsync(string @namespace, string releaseName, string chartName, Dictionary<string, object> values);
    Task UninstallReleaseAsync(string @namespace, string releaseName);
    Task<bool> IsReleaseInstalledAsync(string @namespace, string releaseName);
    Task<Dictionary<string, object>> GetReleaseValuesAsync(string @namespace, string releaseName);
} 