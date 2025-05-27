using Newtonsoft.Json;
using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace PlanescapeStackOperator.Entities;

[KubernetesEntity(Group = "planescape.io", ApiVersion = "v1alpha1", Kind = "PlanescapeStack", PluralName = "planescapestacks")]
public class PlanescapeStack : CustomKubernetesEntity<StackSpec, StackStatus>
{
}

public class StackSpec
{
    [JsonProperty("components")]
    public StackComponents Components { get; set; } = new();
}

public class StackComponents
{
    [JsonProperty("postgresql")]
    public PostgresqlComponent? Postgresql { get; set; }

    [JsonProperty("jenkins")]
    public JenkinsComponent? Jenkins { get; set; }

    [JsonProperty("vault")]
    public VaultComponent? Vault { get; set; }
}

public class PostgresqlComponent
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("auth")]
    public PostgresqlAuth? Auth { get; set; }

    [JsonProperty("primary")]
    public PostgresqlPrimary? Primary { get; set; }
}

public class PostgresqlAuth
{
    [JsonProperty("database")]
    public string? Database { get; set; }

    [JsonProperty("username")]
    public string? Username { get; set; }
}

public class PostgresqlPrimary
{
    [JsonProperty("persistence")]
    public PersistenceConfig? Persistence { get; set; }

    [JsonProperty("resources")]
    public ResourceRequirements? Resources { get; set; }

    [JsonProperty("service")]
    public ServiceConfig? Service { get; set; }
}

public class JenkinsComponent
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("controller")]
    public JenkinsController? Controller { get; set; }
}

public class JenkinsController
{
    [JsonProperty("resources")]
    public ResourceRequirements? Resources { get; set; }

    [JsonProperty("serviceType")]
    public string ServiceType { get; set; } = "ClusterIP";

    [JsonProperty("persistence")]
    public PersistenceConfig? Persistence { get; set; }

    [JsonProperty("installPlugins")]
    public List<string>? InstallPlugins { get; set; }

    [JsonProperty("securityRealm")]
    public string? SecurityRealm { get; set; }

    [JsonProperty("authorizationStrategy")]
    public string? AuthorizationStrategy { get; set; }
}

public class VaultComponent
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("server")]
    public VaultServer? Server { get; set; }
}

public class VaultServer
{
    [JsonProperty("ha")]
    public VaultHA? HA { get; set; }

    [JsonProperty("resources")]
    public ResourceRequirements? Resources { get; set; }

    [JsonProperty("service")]
    public ServiceConfig? Service { get; set; }

    [JsonProperty("dataStorage")]
    public PersistenceConfig? DataStorage { get; set; }
}

public class VaultHA
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("replicas")]
    public int Replicas { get; set; } = 3;
}

public class PersistenceConfig
{
    [JsonProperty("size")]
    public string? Size { get; set; }
}

public class ResourceRequirements
{
    [JsonProperty("requests")]
    public ResourceRequests? Requests { get; set; }

    [JsonProperty("limits")]
    public ResourceLimits? Limits { get; set; }
}

public class ResourceRequests
{
    [JsonProperty("cpu")]
    public string? Cpu { get; set; }

    [JsonProperty("memory")]
    public string? Memory { get; set; }
}

public class ResourceLimits
{
    [JsonProperty("cpu")]
    public string? Cpu { get; set; }

    [JsonProperty("memory")]
    public string? Memory { get; set; }
}

public class ServiceConfig
{
    [JsonProperty("type")]
    public string Type { get; set; } = "ClusterIP";

    [JsonProperty("port")]
    public int Port { get; set; } = 80;
}

public class StackStatus
{
    [JsonProperty("conditions")]
    public List<StackCondition> Conditions { get; set; } = new();

    [JsonProperty("components")]
    public Dictionary<string, ComponentStatus> Components { get; set; } = new();
}

public class StackCondition
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("lastTransitionTime")]
    public DateTime? LastTransitionTime { get; set; }

    [JsonProperty("reason")]
    public string? Reason { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }
}

public class ComponentStatus
{
    [JsonProperty("ready")]
    public bool Ready { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("lastUpdated")]
    public DateTime? LastUpdated { get; set; }
} 