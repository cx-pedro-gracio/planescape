# PostgreSQL secrets policy
path "secret/data/postgres/*" {
  capabilities = ["read"]
}

# Allow PostgreSQL service account to read its own credentials
path "secret/data/postgres/{{identity.entity.aliases.auth_kubernetes_*[0].metadata.service_account_name}}/*" {
  capabilities = ["read"]
}

# Allow PostgreSQL service account to rotate its own credentials
path "secret/data/postgres/{{identity.entity.aliases.auth_kubernetes_*[0].metadata.service_account_name}}/rotate" {
  capabilities = ["create", "update"]
} 