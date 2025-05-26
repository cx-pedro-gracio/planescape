#!/bin/bash

set -euo pipefail

# Directory containing this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ENV_DIR="${SCRIPT_DIR}/../k8s/envs"

# Default to local environment
ENVIRONMENT=${1:-local}

# Validate environment
if [[ ! -d "${ENV_DIR}/${ENVIRONMENT}" ]]; then
    echo "Error: Environment '${ENVIRONMENT}' not found in ${ENV_DIR}"
    echo "Available environments:"
    ls -1 "${ENV_DIR}"
    exit 1
fi

# Create namespaces if they don't exist
kubectl create namespace planescape --dry-run=client -o yaml | kubectl apply -f -
kubectl create namespace jenkins-workers --dry-run=client -o yaml | kubectl apply -f -

# Add Helm repositories
echo "Adding Helm repositories..."
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo add jenkins https://charts.jenkins.io
helm repo update

# Deploy PostgreSQL
echo "Deploying PostgreSQL..."
helm upgrade --install postgres bitnami/postgresql \
    --namespace planescape \
    --wait \
    -f "${ENV_DIR}/${ENVIRONMENT}/postgres-values.yaml"

# Deploy Jenkins
echo "Deploying Jenkins..."
helm upgrade --install jenkins jenkins/jenkins \
    --namespace planescape \
    --wait \
    -f "${ENV_DIR}/${ENVIRONMENT}/jenkins-values.yaml"

echo "Deployment completed successfully!"
echo "To get Jenkins admin password:"
echo "kubectl exec --namespace planescape -it svc/jenkins -c jenkins -- /bin/cat /run/secrets/additional/chart-admin-password"
echo ""
echo "To access Jenkins:"
echo "minikube service jenkins -n planescape" 