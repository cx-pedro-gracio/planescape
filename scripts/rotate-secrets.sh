#!/bin/bash

# Exit on error
set -e

# Function to generate secure password
generate_password() {
    openssl rand -base64 32
}

# Function to rotate PostgreSQL secret
rotate_postgres_secret() {
    echo "Rotating PostgreSQL secret..."
    NEW_PASSWORD=$(generate_password)
    
    kubectl create secret generic postgres-secret \
        --namespace planescape \
        --from-literal=postgres-password="$NEW_PASSWORD" \
        --from-literal=postgres-user='postgres' \
        --from-literal=postgres-database='planescape' \
        --dry-run=client -o yaml | kubectl apply -f -
    
    echo "Restarting PostgreSQL to pick up new credentials..."
    kubectl rollout restart deployment postgres -n planescape
    
    echo "New PostgreSQL password: $NEW_PASSWORD"
}

# Function to rotate Jenkins secret
rotate_jenkins_secret() {
    echo "Rotating Jenkins secret..."
    NEW_PASSWORD=$(generate_password)
    
    kubectl create secret generic jenkins-secret \
        --namespace planescape \
        --from-literal=jenkins-admin-password="$NEW_PASSWORD" \
        --dry-run=client -o yaml | kubectl apply -f -
    
    echo "Restarting Jenkins to pick up new credentials..."
    kubectl rollout restart deployment jenkins -n planescape
    
    echo "New Jenkins admin password: $NEW_PASSWORD"
}

# Main script
case "$1" in
    "postgres")
        rotate_postgres_secret
        ;;
    "jenkins")
        rotate_jenkins_secret
        ;;
    "all")
        rotate_postgres_secret
        echo
        rotate_jenkins_secret
        ;;
    *)
        echo "Usage: $0 {postgres|jenkins|all}"
        echo "  postgres - Rotate PostgreSQL secret"
        echo "  jenkins  - Rotate Jenkins secret"
        echo "  all      - Rotate all secrets"
        exit 1
        ;;
esac

echo "Secret rotation complete!"
echo "Please update any external systems that use these credentials."
echo "Remember to securely store the new passwords." 