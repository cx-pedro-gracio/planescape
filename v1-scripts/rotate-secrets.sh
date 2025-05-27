#!/bin/bash

# Planescape Secret Rotation Script
# This script helps rotate secrets using Vault in a Helm-based deployment

set -euo pipefail

# Configuration
NAMESPACE="${NAMESPACE:-planescape-system}"
RELEASE_NAME="${RELEASE_NAME:-planescape}"
VAULT_ADDR="${VAULT_ADDR:-http://localhost:8200}"
VAULT_TOKEN="${VAULT_TOKEN:-}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Help function
show_help() {
    cat << EOF
Planescape Secret Rotation Script

Usage: $0 [OPTIONS] COMPONENT

Options:
    -n, --namespace NAMESPACE       Kubernetes namespace (default: planescape-system)
    -r, --release NAME             Helm release name (default: planescape)
    -v, --vault-addr ADDR          Vault address (default: http://localhost:8200)
    -t, --vault-token TOKEN        Vault token (default: from VAULT_TOKEN env var)
    -h, --help                     Show this help message

Components:
    postgres                       Rotate PostgreSQL credentials
    jenkins                        Rotate Jenkins admin password
    all                           Rotate all component credentials

Environment Variables:
    NAMESPACE                      Same as --namespace
    RELEASE_NAME                   Same as --release
    VAULT_ADDR                     Same as --vault-addr
    VAULT_TOKEN                    Same as --vault-token

Examples:
    # Rotate PostgreSQL credentials
    $0 postgres

    # Rotate Jenkins password with custom namespace
    $0 --namespace my-namespace jenkins

    # Rotate all credentials with custom Vault address
    $0 --vault-addr http://vault:8200 all

EOF
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl is required but not installed"
        exit 1
    fi
    
    if ! command -v vault &> /dev/null; then
        log_error "vault CLI is required but not installed"
        exit 1
    fi
    
    if ! kubectl cluster-info &> /dev/null; then
        log_error "Cannot connect to Kubernetes cluster"
        exit 1
    fi
    
    # Check Vault connection
    if ! vault status &> /dev/null; then
        log_error "Cannot connect to Vault at $VAULT_ADDR"
        exit 1
    fi
    
    log_success "Prerequisites check passed"
}

# Generate secure password
generate_password() {
    openssl rand -base64 32
}

# Rotate PostgreSQL credentials in Vault
rotate_postgres_secret() {
    log_info "Rotating PostgreSQL credentials in Vault..."
    
    # Generate new password
    NEW_PASSWORD=$(generate_password)
    
    # Update secret in Vault
    vault kv put "secret/${RELEASE_NAME}/postgresql" \
        password="$NEW_PASSWORD" \
        username="postgres" \
        database="postgres"
    
    # Restart PostgreSQL to pick up new credentials
    log_info "Restarting PostgreSQL to pick up new credentials..."
    kubectl rollout restart statefulset -n "$NAMESPACE" "${RELEASE_NAME}-postgresql"
    
    log_success "PostgreSQL credentials rotated successfully"
    log_warning "New PostgreSQL password: $NEW_PASSWORD"
    log_warning "Please update any external systems that use these credentials"
}

# Rotate Jenkins admin password in Vault
rotate_jenkins_secret() {
    log_info "Rotating Jenkins admin password in Vault..."
    
    # Generate new password
    NEW_PASSWORD=$(generate_password)
    
    # Update secret in Vault
    vault kv put "secret/${RELEASE_NAME}/jenkins" \
        admin-password="$NEW_PASSWORD"
    
    # Restart Jenkins to pick up new credentials
    log_info "Restarting Jenkins to pick up new credentials..."
    kubectl rollout restart statefulset -n "$NAMESPACE" "${RELEASE_NAME}-jenkins"
    
    log_success "Jenkins admin password rotated successfully"
    log_warning "New Jenkins admin password: $NEW_PASSWORD"
    log_warning "Please update any external systems that use these credentials"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--namespace)
            NAMESPACE="$2"
            shift 2
            ;;
        -r|--release)
            RELEASE_NAME="$2"
            shift 2
            ;;
        -v|--vault-addr)
            VAULT_ADDR="$2"
            shift 2
            ;;
        -t|--vault-token)
            VAULT_TOKEN="$2"
            shift 2
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            COMPONENT="$1"
            shift
            break
            ;;
    esac
done

# Check if component was provided
if [[ -z "${COMPONENT:-}" ]]; then
    log_error "No component specified"
    show_help
    exit 1
fi

# Set Vault environment
export VAULT_ADDR
if [[ -n "$VAULT_TOKEN" ]]; then
    export VAULT_TOKEN
fi

# Main execution
check_prerequisites

case "$COMPONENT" in
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
        log_error "Unknown component: $COMPONENT"
        show_help
        exit 1
        ;;
esac

log_success "Secret rotation completed!"
log_warning "Remember to securely store the new passwords"
log_warning "Update any external systems that use these credentials" 