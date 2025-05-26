# Jenkins secrets policy
path "secret/data/jenkins/*" {
  capabilities = ["read"]
}

# Allow Jenkins service account to read its own credentials
path "secret/data/jenkins/{{identity.entity.aliases.auth_kubernetes_*[0].metadata.service_account_name}}/*" {
  capabilities = ["read"]
}

# Allow Jenkins service account to rotate its own credentials
path "secret/data/jenkins/{{identity.entity.aliases.auth_kubernetes_*[0].metadata.service_account_name}}/rotate" {
  capabilities = ["create", "update"]
}

# Allow Jenkins to manage worker pod secrets
path "secret/data/jenkins/workers/*" {
  capabilities = ["create", "read", "update", "delete"]
} 