# Vault: Policy for ESO to allow read access to Postgres and Jenkins secrets
path "secret/data/planescape/postgres" {
  capabilities = ["read"]
}

path "secret/data/planescape/jenkins" {
  capabilities = ["read"]
} 