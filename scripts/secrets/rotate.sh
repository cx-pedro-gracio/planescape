#!/bin/bash

set -euo pipefail

# Directory containing this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )/../.." && pwd )"
SECRETS_DIR="${ROOT_DIR}/k8s/secrets"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print status messages
log() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1"
    exit 1
}

# Function to backup existing secrets
backup_secrets() {
    local backup_dir="${SECRETS_DIR}/backup/$(date +%Y%m%d_%H%M%S)"
    mkdir -p "${backup_dir}"
    
    if [[ -f "${SECRETS_DIR}/postgres-secret.yaml" ]]; then
        cp "${SECRETS_DIR}/postgres-secret.yaml" "${backup_dir}/"
    fi
    
    if [[ -f "${SECRETS_DIR}/jenkins-secret.yaml" ]]; then
        cp "${SECRETS_DIR}/jenkins-secret.yaml" "${backup_dir}/"
    fi
    
    log "Secrets backed up to ${backup_dir}"
}

# Function to rotate PostgreSQL secrets
rotate_postgres_secrets() {
    log "Rotating PostgreSQL secrets..."
    
    # Backup existing secrets
    backup_secrets
    
    # Generate new secrets
    "${SCRIPT_DIR}/generate.sh" "${ENVIRONMENT}" "postgres"
    
    # Apply new secrets
    kubectl apply -f "${SECRETS_DIR}/postgres-secret.yaml"
    
    # Restart PostgreSQL pods to pick up new secrets
    kubectl rollout restart statefulset -n planescape postgres-postgresql
    
    log "PostgreSQL secrets rotated successfully"
}

# Function to rotate Jenkins secrets
rotate_jenkins_secrets() {
    log "Rotating Jenkins secrets..."
    
    # Backup existing secrets
    backup_secrets
    
    # Generate new secrets
    "${SCRIPT_DIR}/generate.sh" "${ENVIRONMENT}" "jenkins"
    
    # Apply new secrets
    kubectl apply -f "${SECRETS_DIR}/jenkins-secret.yaml"
    
    # Restart Jenkins pods to pick up new secrets
    kubectl rollout restart deployment -n planescape jenkins
    
    log "Jenkins secrets rotated successfully"
}

# Function to clean up old backups
cleanup_old_backups() {
    local backup_dir="${SECRETS_DIR}/backup"
    if [[ -d "${backup_dir}" ]]; then
        # Keep only the last 5 backups
        find "${backup_dir}" -type d -mtime +5 -exec rm -rf {} +
        log "Cleaned up old secret backups"
    fi
}

# Main script execution
main() {
    # Get environment from argument or default to local
    ENVIRONMENT=${1:-local}
    log "Rotating secrets for environment: ${ENVIRONMENT}"
    
    # Check if kubectl is available
    if ! command -v kubectl &> /dev/null; then
        error "kubectl is not installed"
    fi
    
    # Check if we can access the cluster
    if ! kubectl cluster-info &> /dev/null; then
        error "Cannot access Kubernetes cluster"
    fi
    
    # Rotate secrets based on argument or all if none specified
    case "${2:-all}" in
        "postgres")
            rotate_postgres_secrets
            ;;
        "jenkins")
            rotate_jenkins_secrets
            ;;
        "all")
            rotate_postgres_secrets
            rotate_jenkins_secrets
            ;;
        *)
            error "Unknown secret type: ${2}. Use 'postgres', 'jenkins', or 'all'"
            ;;
    esac
    
    # Cleanup old backups
    cleanup_old_backups
    
    log "Secret rotation completed successfully"
    warn "Remember to update any applications or services that use these secrets"
}

# Run main function with all arguments
main "$@" 