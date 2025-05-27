using System.Text;
using Newtonsoft.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using PlanescapeStackOperator.Entities;
using System.Security.Cryptography;

namespace PlanescapeStackOperator.Services;

public class VaultService : IVaultService
{
    private readonly ILogger<VaultService> _logger;
    private readonly IKubernetes _kubernetes;
    private readonly HttpClient _httpClient;

    public VaultService(ILogger<VaultService> logger, IKubernetes kubernetes, HttpClient httpClient)
    {
        _logger = logger;
        _kubernetes = kubernetes;
        _httpClient = httpClient;
    }

    public async Task InitializeVaultAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing Vault for stack {StackName}", stack.Metadata.Name);
        
        // Get Vault pod IP
        var vaultPod = await _kubernetes.CoreV1.ListNamespacedPodAsync(
            stack.Metadata.NamespaceProperty,
            labelSelector: $"app.kubernetes.io/instance={stack.Metadata.Name}-vault-operator");
        
        if (!vaultPod.Items.Any())
        {
            throw new Exception($"Vault pod not found for stack {stack.Metadata.Name}");
        }
        
        string? localVault = Environment.GetEnvironmentVariable("VAULT_LOCAL_PORT_FORWARD");
        if (!string.IsNullOrEmpty(localVault) && localVault == "true")
        {
            _httpClient.BaseAddress = new Uri("http://localhost:8200/");
            _logger.LogInformation("Using Vault at http://localhost:8200/ (local port-forward mode)");
        }
        else
        {
            var vaultPodIp = vaultPod.Items[0].Status.PodIP;
            _httpClient.BaseAddress = new Uri($"http://{vaultPodIp}:8200/");
            _logger.LogInformation("Using Vault at {VaultUrl}", _httpClient.BaseAddress);
        }

        // Wait for Vault to be reachable (but not necessarily ready)
        await WaitForVaultReachableAsync(cancellationToken);

        // Get Vault status to determine current state
        var status = await GetVaultStatusAsync();
        _logger.LogInformation("Vault status: Initialized={Initialized}, Sealed={Sealed}", status.Initialized, status.Sealed);
        
        // Check for existing unseal key secret
        V1Secret? unsealKeySecret = null;
        try
        {
            unsealKeySecret = await _kubernetes.CoreV1.ReadNamespacedSecretAsync(
            $"{stack.Metadata.Name}-vault-operator-unseal-key",
                stack.Metadata.NamespaceProperty);
        }
        catch (Exception ex) when (ex.Message.Contains("NotFound"))
        {
            _logger.LogInformation("No existing unseal key secret found");
            }
            
        // Determine if we need to reinitialize Vault
        bool needsReinitialization = await DetermineIfReinitializationNeeded(status, unsealKeySecret, stack, cancellationToken);
        
        if (needsReinitialization)
        {
            _logger.LogWarning("Vault needs reinitialization due to corrupted or inconsistent state");
            await ReinitializeVaultAsync(stack, cancellationToken);
        }
        else if (!status.Initialized)
        {
            _logger.LogInformation("Vault is not initialized, performing fresh initialization");
            await PerformFreshInitializationAsync(stack, cancellationToken);
            }
        else if (status.Sealed)
            {
            _logger.LogInformation("Vault is initialized but sealed, attempting to unseal");
            await UnsealVaultAsync(unsealKeySecret!, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Vault is already initialized and unsealed");
            // Set root token for future requests
            var rootToken = Encoding.UTF8.GetString(unsealKeySecret!.Data["root-token"]);
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Vault-Token", rootToken);
        }

        // Wait for Vault to be fully ready
        await WaitForVaultReadyAsync(cancellationToken);
        
        // Enable database secrets engine
        if (!await IsMountEnabledAsync("database", cancellationToken))
        {
            _logger.LogInformation("Enabling database secrets engine");
            var enableDbResponse = await _httpClient.PostAsync("/v1/sys/mounts/database", new StringContent(
                JsonConvert.SerializeObject(new { type = "database" }),
                Encoding.UTF8,
                "application/json"), cancellationToken);
            
            if (!enableDbResponse.IsSuccessStatusCode)
            {
                var errorContent = await enableDbResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to enable database secrets engine. Status: {Status}, Error: {Error}", 
                    enableDbResponse.StatusCode, errorContent);
                enableDbResponse.EnsureSuccessStatusCode();
            }
            _logger.LogInformation("Database secrets engine enabled successfully");
        }
        else
        {
            _logger.LogInformation("Database secrets engine already enabled");
        }
        
        // Enable KV v2 secrets engine for storing application secrets
        if (!await IsMountEnabledAsync("secret", cancellationToken))
        {
            _logger.LogInformation("Enabling KV v2 secrets engine");
            var enableKvResponse = await _httpClient.PostAsync("/v1/sys/mounts/secret", new StringContent(
                JsonConvert.SerializeObject(new { 
                    type = "kv",
                    options = new { version = "2" }
                }),
                Encoding.UTF8,
                "application/json"), cancellationToken);
            
            if (!enableKvResponse.IsSuccessStatusCode)
            {
                var errorContent = await enableKvResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to enable KV v2 secrets engine. Status: {Status}, Error: {Error}", 
                    enableKvResponse.StatusCode, errorContent);
                enableKvResponse.EnsureSuccessStatusCode();
            }
            _logger.LogInformation("KV v2 secrets engine enabled successfully");
        }
        else
        {
            _logger.LogInformation("KV v2 secrets engine already enabled");
        }
        
        _logger.LogInformation("Vault initialization completed successfully");
    }

    private async Task WaitForVaultReachableAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Waiting for Vault to be reachable...");
        
        while (true)
        {
            try
            {
                var response = await _httpClient.GetAsync("/v1/sys/health", cancellationToken);
                // 200 OK, 503 Service Unavailable, or 501 Not Implemented are all valid responses from Vault
                if (response.IsSuccessStatusCode || 
                    response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                    response.StatusCode == System.Net.HttpStatusCode.NotImplemented)
                {
                    _logger.LogInformation("Vault is reachable (status: {StatusCode})", response.StatusCode);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Vault not yet reachable: {Error}", ex.Message);
            }
            
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task<bool> DetermineIfReinitializationNeeded(VaultStatus status, V1Secret? unsealKeySecret, PlanescapeStack stack, CancellationToken cancellationToken)
    {
        // If Vault is not initialized and we have no secret, fresh init is needed (not reinitialization)
        if (!status.Initialized && unsealKeySecret == null)
        {
            return false;
        }

        // If Vault is not initialized but we have an old secret, we need to clean up and reinitialize
        if (!status.Initialized && unsealKeySecret != null)
        {
            _logger.LogWarning("Vault is not initialized but unseal key secret exists - indicates corrupted state");
            return true;
        }

        // If we have no secret but Vault claims to be initialized, something is wrong
        if (status.Initialized && unsealKeySecret == null)
        {
            _logger.LogWarning("Vault claims to be initialized but no unseal key secret found - indicates corrupted state");
            return true;
        }

        // If both exist, validate that the secret actually works with this Vault instance
        if (status.Initialized && unsealKeySecret != null)
        {
            return await ValidateUnsealKeysAsync(unsealKeySecret, cancellationToken);
        }

        return false;
    }

    private async Task<bool> ValidateUnsealKeysAsync(V1Secret unsealKeySecret, CancellationToken cancellationToken)
    {
        try
        {
            // Check if secret has required data
            if (unsealKeySecret.Data == null || 
                !unsealKeySecret.Data.TryGetValue("unseal-keys", out _) ||
                !unsealKeySecret.Data.TryGetValue("root-token", out _))
            {
                _logger.LogWarning("Unseal key secret is missing required data");
                return true; // Needs reinitialization
            }

            // If Vault is sealed, try to unseal with existing keys to validate they work
            var currentStatus = await GetVaultStatusAsync();
            if (currentStatus.Sealed)
            {
                var unsealKeysJson = Encoding.UTF8.GetString(unsealKeySecret.Data["unseal-keys"]);
                var unsealKeys = JsonConvert.DeserializeObject<string[]>(unsealKeysJson);
                
                if (unsealKeys == null || unsealKeys.Length < 3)
                {
                    _logger.LogWarning("Invalid unseal keys format");
                    return true; // Needs reinitialization
                }

                // Try to unseal with the first key to validate it works
                var testUnsealResponse = await _httpClient.PutAsync("/v1/sys/unseal", new StringContent(
                    JsonConvert.SerializeObject(new { key = unsealKeys[0] }),
                    Encoding.UTF8,
                    "application/json"), cancellationToken);

                if (!testUnsealResponse.IsSuccessStatusCode)
                {
                    var errorContent = await testUnsealResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Unseal key validation failed: {Error}", errorContent);
                    return true; // Needs reinitialization
                }

                // If we successfully used one key, continue with the rest to fully unseal
                foreach (var key in unsealKeys.Skip(1).Take(2))
                {
                    var unsealResponse = await _httpClient.PutAsync("/v1/sys/unseal", new StringContent(
                        JsonConvert.SerializeObject(new { key }),
                        Encoding.UTF8,
                        "application/json"), cancellationToken);
                    
                    if (!unsealResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Failed to unseal with key during validation");
                        return true; // Needs reinitialization
                    }
                }
            }

            // Validate root token works
            var rootToken = Encoding.UTF8.GetString(unsealKeySecret.Data["root-token"]);
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Vault-Token", rootToken);

            var tokenValidationResponse = await _httpClient.GetAsync("/v1/auth/token/lookup-self", cancellationToken);
            if (!tokenValidationResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Root token validation failed");
                return true; // Needs reinitialization
            }

            _logger.LogInformation("Existing unseal keys and root token validated successfully");
            return false; // No reinitialization needed
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validating unseal keys - will reinitialize");
            return true; // Needs reinitialization
        }
    }

    private async Task ReinitializeVaultAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reinitializing Vault due to corrupted state");

        // Clean up old secret
        try
        {
            await _kubernetes.CoreV1.DeleteNamespacedSecretAsync(
                $"{stack.Metadata.Name}-vault-operator-unseal-key",
                stack.Metadata.NamespaceProperty);
            _logger.LogInformation("Deleted old unseal key secret");
        }
        catch (Exception ex) when (ex.Message.Contains("NotFound"))
        {
            _logger.LogInformation("No old unseal key secret to delete");
        }

        // Delete PVC to clear corrupted data
        try
        {
            await _kubernetes.CoreV1.DeleteNamespacedPersistentVolumeClaimAsync(
                $"data-{stack.Metadata.Name}-vault-operator-0",
                stack.Metadata.NamespaceProperty);
            _logger.LogInformation("Deleted Vault PVC to clear corrupted data");
        }
        catch (Exception ex) when (ex.Message.Contains("NotFound"))
        {
            _logger.LogInformation("No Vault PVC to delete");
        }

        // Delete Vault pod to force recreation with fresh storage
        var vaultPods = await _kubernetes.CoreV1.ListNamespacedPodAsync(
            stack.Metadata.NamespaceProperty,
            labelSelector: $"app.kubernetes.io/instance={stack.Metadata.Name}-vault-operator");

        foreach (var pod in vaultPods.Items)
        {
            await _kubernetes.CoreV1.DeleteNamespacedPodAsync(pod.Metadata.Name, stack.Metadata.NamespaceProperty);
            _logger.LogInformation("Deleted Vault pod {PodName}", pod.Metadata.Name);
        }

        // Wait for pod to be recreated
        _logger.LogInformation("Waiting for Vault pod to be recreated...");
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        // Wait for new pod to be ready
        await WaitForVaultPodReadyAsync(stack, cancellationToken);

        // Wait for Vault service to be reachable
        await WaitForVaultReachableAsync(cancellationToken);

        // Perform fresh initialization
        await PerformFreshInitializationAsync(stack, cancellationToken);
    }

    private async Task PerformFreshInitializationAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing fresh Vault initialization");

        // Initialize Vault
        var initResponse = await _httpClient.PutAsync("/v1/sys/init", new StringContent(
            JsonConvert.SerializeObject(new { secret_shares = 5, secret_threshold = 3 }),
            Encoding.UTF8,
            "application/json"), cancellationToken);
        
        initResponse.EnsureSuccessStatusCode();
        var initResult = JsonConvert.DeserializeObject<VaultInitResponse>(
            await initResponse.Content.ReadAsStringAsync(cancellationToken));
        
        if (initResult == null)
        {
            throw new Exception("Failed to parse Vault initialization response");
        }

        // Store unseal keys and root token
        await StoreUnsealKeysAndTokenAsync(stack, initResult, cancellationToken);

        // Unseal Vault
        await UnsealVaultWithKeysAsync(initResult.Keys, cancellationToken);

        // Set root token for future requests
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-Vault-Token", initResult.RootToken);

        _logger.LogInformation("Fresh Vault initialization completed");
    }

    private async Task UnsealVaultAsync(V1Secret unsealKeySecret, CancellationToken cancellationToken)
    {
        var unsealKeysJson = Encoding.UTF8.GetString(unsealKeySecret.Data["unseal-keys"]);
        var unsealKeys = JsonConvert.DeserializeObject<string[]>(unsealKeysJson);
        
        if (unsealKeys == null)
        {
            throw new Exception("Failed to parse unseal keys");
        }

        await UnsealVaultWithKeysAsync(unsealKeys, cancellationToken);

        // Set root token for future requests
        var rootToken = Encoding.UTF8.GetString(unsealKeySecret.Data["root-token"]);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-Vault-Token", rootToken);
    }

    private async Task UnsealVaultWithKeysAsync(string[] unsealKeys, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Unsealing Vault with {KeyCount} keys", Math.Min(3, unsealKeys.Length));

        foreach (var key in unsealKeys.Take(3))
        {
            var unsealResponse = await _httpClient.PutAsync("/v1/sys/unseal", new StringContent(
                JsonConvert.SerializeObject(new { key }),
                Encoding.UTF8,
                "application/json"), cancellationToken);
            
            unsealResponse.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Vault unsealed successfully");
    }

    private async Task StoreUnsealKeysAndTokenAsync(PlanescapeStack stack, VaultInitResponse initResult, CancellationToken cancellationToken)
    {
        var unsealKeysJson = JsonConvert.SerializeObject(initResult.Keys);
        var unsealKeysBytes = Encoding.UTF8.GetBytes(unsealKeysJson);
        var rootTokenBytes = Encoding.UTF8.GetBytes(initResult.RootToken);
        
        var secret = new V1Secret
        {
            Metadata = new V1ObjectMeta
            {
                Name = $"{stack.Metadata.Name}-vault-operator-unseal-key",
                NamespaceProperty = stack.Metadata.NamespaceProperty
            },
            Data = new Dictionary<string, byte[]>
            {
                ["unseal-keys"] = unsealKeysBytes,
                ["root-token"] = rootTokenBytes
            }
        };
        
        try
        {
            await _kubernetes.CoreV1.CreateNamespacedSecretAsync(secret, stack.Metadata.NamespaceProperty, cancellationToken: cancellationToken);
            _logger.LogInformation("Created unseal key secret");
        }
        catch (Exception ex) when (ex.Message.Contains("AlreadyExists"))
        {
            await _kubernetes.CoreV1.ReplaceNamespacedSecretAsync(secret, secret.Metadata.Name, secret.Metadata.NamespaceProperty, cancellationToken: cancellationToken);
            _logger.LogInformation("Updated existing unseal key secret");
        }
    }

    private async Task WaitForVaultPodReadyAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Waiting for Vault pod to be ready...");
        
        while (true)
        {
            var vaultPods = await _kubernetes.CoreV1.ListNamespacedPodAsync(
                stack.Metadata.NamespaceProperty,
                labelSelector: $"app.kubernetes.io/instance={stack.Metadata.Name}-vault-operator",
                cancellationToken: cancellationToken);
            
            if (vaultPods.Items.Any() && 
                vaultPods.Items.All(p => p.Status.Phase == "Running" && 
                                        p.Status.ContainerStatuses?.All(c => c.Ready) == true))
            {
                _logger.LogInformation("Vault pod is ready");
                break;
            }
            
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    public async Task ConfigurePostgresqlDatabaseAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring PostgreSQL database connection in Vault");
        
        // Get the actual PostgreSQL password from the Kubernetes secret
        var postgresSecret = await _kubernetes.CoreV1.ReadNamespacedSecretAsync(
            $"{stack.Metadata.Name}-postgresql",
            stack.Metadata.NamespaceProperty,
            cancellationToken: cancellationToken);
            
        if (postgresSecret?.Data == null || !postgresSecret.Data.TryGetValue("postgres-password", out var passwordBytes))
        {
            throw new Exception($"PostgreSQL password not found in secret {stack.Metadata.Name}-postgresql");
        }
        
        var postgresPassword = Encoding.UTF8.GetString(passwordBytes);
        
        // Get the PostgreSQL service to find the ClusterIP
        var postgresService = await _kubernetes.CoreV1.ReadNamespacedServiceAsync(
            $"{stack.Metadata.Name}-postgresql",
            stack.Metadata.NamespaceProperty,
            cancellationToken: cancellationToken);
            
        if (postgresService?.Spec?.ClusterIP == null)
        {
            throw new Exception($"PostgreSQL service ClusterIP not found for {stack.Metadata.Name}-postgresql");
        }
        
        var clusterIP = postgresService.Spec.ClusterIP;
        
        // Configure PostgreSQL connection using ClusterIP and postgres database
        _logger.LogInformation("Configuring PostgreSQL connection using ClusterIP {ClusterIP}", clusterIP);
        var configDbResponse = await _httpClient.PostAsync("/v1/database/config/postgresql", new StringContent(
            JsonConvert.SerializeObject(new
            {
                plugin_name = "postgresql-database-plugin",
                allowed_roles = new[] { "planescape" },
                connection_url = $"postgresql://{{{{username}}}}:{{{{password}}}}@{clusterIP}:5432/postgres?sslmode=disable",
                username = "postgres",
                password = postgresPassword,
                verify_connection = false  // Skip connection verification for now
            }),
            Encoding.UTF8,
            "application/json"), cancellationToken);
        
        if (!configDbResponse.IsSuccessStatusCode)
        {
            var errorContent = await configDbResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to configure PostgreSQL connection. Status: {Status}, Error: {Error}", 
                configDbResponse.StatusCode, errorContent);
            configDbResponse.EnsureSuccessStatusCode();
        }
        _logger.LogInformation("PostgreSQL connection configured successfully");
        
        // Create database role
        _logger.LogInformation("Creating database role 'planescape'");
        var createRoleResponse = await _httpClient.PostAsync("/v1/database/roles/planescape", new StringContent(
            JsonConvert.SerializeObject(new
            {
                db_name = "postgresql",
                creation_statements = new[]
                {
                    "CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}';",
                    "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO \"{{name}}\";",
                    "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO \"{{name}}\";"
                },
                default_ttl = "1h",
                max_ttl = "24h"
            }),
            Encoding.UTF8,
            "application/json"), cancellationToken);
        
        if (!createRoleResponse.IsSuccessStatusCode)
        {
            var errorContent = await createRoleResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create database role. Status: {Status}, Error: {Error}", 
                createRoleResponse.StatusCode, errorContent);
            createRoleResponse.EnsureSuccessStatusCode();
        }
        _logger.LogInformation("Database role 'planescape' created successfully");
    }

    public async Task ConfigureVaultAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        // Store Jenkins credentials
        _logger.LogInformation("Storing Jenkins credentials");
        var jenkinsAdminPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var jenkinsAgentPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        
        await StoreSecretAsync(
            stack.Metadata.NamespaceProperty,
            "jenkins",
            "admin-password",
            jenkinsAdminPassword,
            cancellationToken);
        
        await StoreSecretAsync(
            stack.Metadata.NamespaceProperty,
            "jenkins",
            "agent-password",
            jenkinsAgentPassword,
            cancellationToken);
        
        _logger.LogInformation("Vault configured successfully for stack {StackName}", stack.Metadata.Name);
    }

    private async Task WaitForVaultReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await _httpClient.GetAsync("/v1/sys/health", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var health = JsonConvert.DeserializeObject<VaultHealthResponse>(content);
            if (health == null)
            {
                _logger.LogWarning("Could not parse Vault health response: {Content}", content);
                await Task.Delay(2000, cancellationToken);
                continue;
            }
            if (!health.Initialized)
            {
                _logger.LogInformation("Vault not initialized, waiting...");
                await Task.Delay(2000, cancellationToken);
                continue;
            }
            if (health.Sealed)
            {
                _logger.LogInformation("Vault is sealed, waiting for unseal...");
                await Task.Delay(2000, cancellationToken);
                continue;
            }
            if (health.Standby)
            {
                _logger.LogInformation("Vault is standby, waiting for active...");
                await Task.Delay(2000, cancellationToken);
                continue;
            }
            _logger.LogInformation("Vault is initialized, unsealed, and active.");
            break;
        }
    }

    private async Task<VaultStatus> GetVaultStatusAsync()
    {
        var response = await _httpClient.GetAsync("/v1/sys/init");
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Vault status response: {Status}", content);
        
        try
        {
            return JsonConvert.DeserializeObject<VaultStatus>(content) 
                ?? throw new Exception("Failed to parse Vault status - got null");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Vault status response: {Content}", content);
            throw;
        }
    }

    private class VaultInitResponse
    {
        [JsonProperty("keys")]
        public string[] Keys { get; set; } = Array.Empty<string>();

        [JsonProperty("root_token")]
        public string RootToken { get; set; } = string.Empty;
    }

    private class VaultStatus
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("initialized")]
        public bool Initialized { get; set; }

        [JsonProperty("sealed")]
        public bool Sealed { get; set; }

        [JsonProperty("t")]
        public int Threshold { get; set; }

        [JsonProperty("n")]
        public int Shares { get; set; }

        [JsonProperty("progress")]
        public int Progress { get; set; }

        [JsonProperty("nonce")]
        public string Nonce { get; set; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("build_date")]
        public string BuildDate { get; set; } = string.Empty;

        [JsonProperty("migration")]
        public bool Migration { get; set; }

        [JsonProperty("cluster_name")]
        public string ClusterName { get; set; } = string.Empty;

        [JsonProperty("cluster_id")]
        public string ClusterId { get; set; } = string.Empty;

        [JsonProperty("recovery_seal")]
        public bool RecoverySeal { get; set; }

        [JsonProperty("storage_type")]
        public string StorageType { get; set; } = string.Empty;

        [JsonProperty("ha_enabled")]
        public bool HaEnabled { get; set; }

        [JsonProperty("active_time")]
        public string ActiveTime { get; set; } = string.Empty;
    }

    public Task ConfigureJobVaultAccessAsync(PlanescapeStack stack, PlanescapeJob job, CancellationToken cancellationToken)
    {
        // For now, we don't need to configure job access since we're using Kubernetes secrets instead
        _logger.LogInformation("Job Vault access not configured - using Kubernetes secrets instead");
        return Task.CompletedTask;
    }

    public async Task CleanupVaultAsync(PlanescapeStack stack, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up Vault for stack {StackName}", stack.Metadata.Name);
        
        try
        {
        // Delete the unseal key secret
            var secretName = $"{stack.Metadata.Name}-vault-operator-unseal-key";
            try
            {
                await _kubernetes.CoreV1.DeleteNamespacedSecretAsync(
                    secretName,
                    stack.Metadata.NamespaceProperty,
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Deleted Vault unseal key secret {SecretName}", secretName);
            }
            catch (Exception ex) when (ex.Message.Contains("NotFound"))
            {
                _logger.LogInformation("Vault unseal key secret {SecretName} does not exist, skipping deletion", secretName);
            }

            // Delete any Vault-related PVCs
            var pvcName = $"data-{stack.Metadata.Name}-vault-operator-0";
            try
            {
                await _kubernetes.CoreV1.DeleteNamespacedPersistentVolumeClaimAsync(
                    pvcName,
                    stack.Metadata.NamespaceProperty,
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Deleted Vault PVC {PvcName}", pvcName);
            }
            catch (Exception ex) when (ex.Message.Contains("NotFound"))
            {
                _logger.LogInformation("Vault PVC {PvcName} does not exist, skipping deletion", pvcName);
            }

            // Clean up any other Vault-related secrets (PostgreSQL credentials stored in Vault, etc.)
            var vaultSecretsToClean = new[]
            {
                $"{stack.Metadata.Name}-vault-postgresql-credentials",
                $"{stack.Metadata.Name}-vault-jenkins-credentials"
            };

            foreach (var vaultSecretName in vaultSecretsToClean)
            {
        try
        {
            await _kubernetes.CoreV1.DeleteNamespacedSecretAsync(
                        vaultSecretName,
                        stack.Metadata.NamespaceProperty,
                        cancellationToken: cancellationToken);
                    _logger.LogInformation("Deleted Vault-related secret {SecretName}", vaultSecretName);
        }
        catch (Exception ex) when (ex.Message.Contains("NotFound"))
        {
                    _logger.LogDebug("Vault-related secret {SecretName} does not exist, skipping deletion", vaultSecretName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error deleting Vault-related secret {SecretName}, continuing cleanup", vaultSecretName);
                }
            }

            _logger.LogInformation("Vault cleanup completed for stack {StackName}", stack.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Vault cleanup for stack {StackName}", stack.Metadata.Name);
            throw;
        }
    }

    public async Task<string> GetVaultTokenAsync(string @namespace, string releaseName, CancellationToken cancellationToken)
    {
        // Get the root token from the secret
        var secret = await _kubernetes.CoreV1.ReadNamespacedSecretAsync(
            $"{releaseName}-unseal-key",
            @namespace);
        
        if (secret?.Data == null || !secret.Data.TryGetValue("root-token", out var tokenBytes))
        {
            throw new Exception("Root token not found");
        }

        return Encoding.UTF8.GetString(tokenBytes);
    }

    public async Task StoreSecretAsync(string @namespace, string path, string key, string value, CancellationToken cancellationToken)
    {
        await _httpClient.PutAsync(
            $"/v1/secret/data/{@namespace}/{path}",
            new StringContent(JsonConvert.SerializeObject(new { data = new Dictionary<string, string> { [key] = value } }), Encoding.UTF8, "application/json"));
    }

    public async Task<string> GetSecretAsync(string @namespace, string path, string key, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/v1/secret/data/{@namespace}/{path}");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Vault secret response: {Content}", content);

        var secretResponse = JsonConvert.DeserializeObject<VaultKvV2Response>(content);

        if (secretResponse?.Data?.Data == null || !secretResponse.Data.Data.TryGetValue(key, out var value))
        {
            throw new Exception($"Secret {key} not found at path {path}");
        }

        return value;
    }

    public async Task<bool> IsVaultReadyAsync(string @namespace, string releaseName, CancellationToken cancellationToken)
    {
        try
        {
            var pod = await _kubernetes.CoreV1.ReadNamespacedPodAsync(
                $"{releaseName}-0",
                @namespace,
                cancellationToken: cancellationToken);

            return pod.Status.Phase == "Running" &&
                   pod.Status.ContainerStatuses?.All(c => c.Ready) == true;
        }
        catch
        {
            return false;
        }
    }

    private class VaultKvV2Response
    {
        [JsonProperty("data")]
        public VaultKvV2Data? Data { get; set; }
    }

    private class VaultKvV2Data
    {
        [JsonProperty("data")]
        public Dictionary<string, string>? Data { get; set; }
        
        [JsonProperty("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class VaultSecretResponse
    {
        [JsonProperty("data")]
        public Dictionary<string, string>? Data { get; set; }
    }

    private class VaultHealthResponse
    {
        [JsonProperty("initialized")]
        public bool Initialized { get; set; }
        [JsonProperty("sealed")]
        public bool Sealed { get; set; }
        [JsonProperty("standby")]
        public bool Standby { get; set; }
    }

    private async Task<bool> IsMountEnabledAsync(string mountPath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("/v1/sys/mounts", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get mounts list. Status: {Status}", response.StatusCode);
                return false;
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var mounts = JsonConvert.DeserializeObject<VaultMountsResponse>(content);
            
            if (mounts?.Data == null)
            {
                _logger.LogWarning("Could not parse mounts response");
                return false;
            }
            
            // Check if the mount exists (with or without trailing slash)
            return mounts.Data.ContainsKey($"{mountPath}/") || mounts.Data.ContainsKey(mountPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if mount {MountPath} is enabled", mountPath);
            return false;
        }
    }

    private class VaultMountsResponse
    {
        [JsonProperty("data")]
        public Dictionary<string, object>? Data { get; set; }
    }
} 