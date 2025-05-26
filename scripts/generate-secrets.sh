#!/bin/bash

set -euo pipefail

# Directory containing this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SECRETS_DIR="${SCRIPT_DIR}/../k8s/secrets"

# Create secrets directory if it doesn't exist
mkdir -p "${SECRETS_DIR}"

# Generate random passwords
POSTGRES_PASSWORD=$(openssl rand -base64 32)
JENKINS_ADMIN_PASSWORD=$(openssl rand -base64 32)

# PostgreSQL Secret
cat > "${SECRETS_DIR}/postgres-secret.yaml" << EOF
apiVersion: v1
kind: Secret
metadata:
  name: postgres-secret
  namespace: planescape
type: Opaque
data:
  postgres-password: $(echo -n "${POSTGRES_PASSWORD}" | base64)
EOF

# Jenkins Secret
cat > "${SECRETS_DIR}/jenkins-secret.yaml" << EOF
apiVersion: v1
kind: Secret
metadata:
  name: jenkins-secret
  namespace: planescape
type: Opaque
data:
  jenkins-admin-password: $(echo -n "${JENKINS_ADMIN_PASSWORD}" | base64)
EOF

echo "Secrets generated successfully in ${SECRETS_DIR}"
echo "Please ensure these files are not committed to version control"
echo "You can now apply these secrets using: kubectl apply -f ${SECRETS_DIR}/" 