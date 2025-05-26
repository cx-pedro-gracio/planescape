#!/bin/bash

# Exit on error
set -e

echo "Setting up local environment..."

# Start Minikube if not running
if ! minikube status | grep -q "Running"; then
    echo "Starting Minikube..."
    minikube start --driver=docker --cpus=4 --memory=8g
    minikube addons enable ingress
    minikube addons enable storage-provisioner
fi

# Create namespaces
echo "Creating namespaces..."
kubectl create namespace planescape --dry-run=client -o yaml | kubectl apply -f -
kubectl create namespace jenkins-workers --dry-run=client -o yaml | kubectl apply -f -

# Generate secure passwords
POSTGRES_PASSWORD=$(openssl rand -base64 32)
JENKINS_PASSWORD=$(openssl rand -base64 32)

# Create secrets
echo "Creating secrets..."
kubectl create secret generic postgres-secret \
    --namespace planescape \
    --from-literal=postgres-password="$POSTGRES_PASSWORD" \
    --from-literal=postgres-user='postgres' \
    --from-literal=postgres-database='planescape' \
    --dry-run=client -o yaml | kubectl apply -f -

kubectl create secret generic jenkins-secret \
    --namespace planescape \
    --from-literal=jenkins-admin-password="$JENKINS_PASSWORD" \
    --dry-run=client -o yaml | kubectl apply -f -

# Add Helm repos
echo "Adding Helm repositories..."
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo add jenkins https://charts.jenkins.io
helm repo update

# Deploy PostgreSQL
echo "Deploying PostgreSQL..."
helm upgrade --install postgres bitnami/postgresql \
    --namespace planescape \
    -f ../k8s/base/postgres/values.yaml \
    -f ../k8s/envs/local/postgres.yaml

# Deploy Jenkins
echo "Deploying Jenkins..."
helm upgrade --install jenkins jenkins/jenkins \
    --namespace planescape \
    -f ../k8s/base/jenkins/values.yaml \
    -f ../k8s/envs/local/jenkins.yaml

echo "Setup complete!"
echo "Jenkins admin password: $JENKINS_PASSWORD"
echo "PostgreSQL password: $POSTGRES_PASSWORD"
echo
echo "To access Jenkins:"
echo "minikube service jenkins -n planescape"
echo
echo "To access PostgreSQL:"
echo "kubectl port-forward -n planescape svc/postgres 5432:5432" 