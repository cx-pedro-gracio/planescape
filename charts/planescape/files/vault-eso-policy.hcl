# Vault: Policy for ESO to allow read access to Postgres and Jenkins secrets

# Allow ESO to authenticate and renew tokens
path "auth/token/lookup-self" {
  capabilities = ["read"]
}

path "auth/token/renew-self" {
  capabilities = ["update"]
}

# Allow reading secrets
path "secret/data/planescape/postgres" {
  capabilities = ["read"]
}

path "secret/data/planescape/jenkins" {
  capabilities = ["read"]
}

path "secret/metadata/planescape/*" {
  capabilities = ["list", "read"]
} 