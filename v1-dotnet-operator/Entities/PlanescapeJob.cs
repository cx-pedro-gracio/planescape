using Newtonsoft.Json;
using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace PlanescapeStackOperator.Entities;

[KubernetesEntity(Group = "planescape.io", ApiVersion = "v1alpha1", Kind = "PlanescapeJob", PluralName = "planescapejobs")]
public class PlanescapeJob : CustomKubernetesEntity<JobSpec, JobStatus>
{
}

public class JobSpec
{
    [JsonProperty("schedule")]
    public string? Schedule { get; set; }

    [JsonProperty("concurrencyPolicy")]
    public string? ConcurrencyPolicy { get; set; }

    [JsonProperty("successfulJobsHistoryLimit")]
    public int? SuccessfulJobsHistoryLimit { get; set; }

    [JsonProperty("failedJobsHistoryLimit")]
    public int? FailedJobsHistoryLimit { get; set; }

    [JsonProperty("jobTemplate")]
    public V1JobTemplateSpec JobTemplate { get; set; } = new();
}

public class JobStatus
{
    [JsonProperty("conditions")]
    public List<JobCondition> Conditions { get; set; } = new();
}

public class JobCondition
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("lastTransitionTime")]
    public DateTime LastTransitionTime { get; set; }
} 