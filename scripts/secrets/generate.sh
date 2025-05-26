#!/bin/bash

set -euo pipefail

# Directory containing this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )/../.." && pwd )"
SECRETS_DIR="${ROOT_DIR}/k8s/secrets"
TEMPLATES_DIR="${SECRETS_DIR}/templates"

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

# Function to generate a random password
generate_password() {
    openssl rand -base64 32
}

# Function to generate PostgreSQL secrets
generate_postgres_secrets() {
    log "Generating PostgreSQL secrets..."
    
    # Generate passwords
    local POSTGRES_PASSWORD=$(generate_password)
    local POSTGRES_BACKUP_PASSWORD=$(generate_password)
    local POSTGRES_MONITORING_PASSWORD=$(generate_password)
    
    # Create secret file
    cat > "${SECRETS_DIR}/postgres-secret.yaml" << EOF
apiVersion: v1
kind: Secret
metadata:
  name: postgres-secret
  namespace: planescape
  labels:
    app: postgres
    environment: ${ENVIRONMENT:-local}
type: Opaque
data:
  postgres-password: $(echo -n "${POSTGRES_PASSWORD}" | base64)
  backup-password: $(echo -n "${POSTGRES_BACKUP_PASSWORD}" | base64)
  monitoring-password: $(echo -n "${POSTGRES_MONITORING_PASSWORD}" | base64)
EOF

    log "PostgreSQL secrets generated successfully"
}

# Function to generate Jenkins secrets
generate_jenkins_secrets() {
    log "Generating Jenkins secrets..."
    
    # Generate passwords
    local JENKINS_ADMIN_PASSWORD=$(generate_password)
    local JENKINS_AGENT_PASSWORD=$(generate_password)
    
    # Create secret file
    cat > "${SECRETS_DIR}/jenkins-secret.yaml" << EOF
apiVersion: v1
kind: Secret
metadata:
  name: jenkins-secret
  namespace: planescape
  labels:
    app: jenkins
    environment: ${ENVIRONMENT:-local}
type: Opaque
data:
  jenkins-admin-password: $(echo -n "${JENKINS_ADMIN_PASSWORD}" | base64)
  jenkins-agent-password: $(echo -n "${JENKINS_AGENT_PASSWORD}" | base64)
EOF

    log "Jenkins secrets generated successfully"
}

# Main script execution
main() {
    # Check if secrets directory exists
    if [[ ! -d "${SECRETS_DIR}" ]]; then
        mkdir -p "${SECRETS_DIR}"
        log "Created secrets directory at ${SECRETS_DIR}"
    fi

    # Check if templates directory exists
    if [[ ! -d "${TEMPLATES_DIR}" ]]; then
        mkdir -p "${TEMPLATES_DIR}"
        log "Created templates directory at ${TEMPLATES_DIR}"
    fi

    # Get environment from argument or default to local
    ENVIRONMENT=${1:-local}
    log "Generating secrets for environment: ${ENVIRONMENT}"

    # Generate secrets based on argument or all if none specified
    case "${2:-all}" in
        "postgres")
            generate_postgres_secrets
            ;;
        "jenkins")
            generate_jenkins_secrets
            ;;
        "all")
            generate_postgres_secrets
            generate_jenkins_secrets
            ;;
        *)
            error "Unknown secret type: ${2}. Use 'postgres', 'jenkins', or 'all'"
            ;;
    esac

    # Final instructions
    log "Secrets generated successfully in ${SECRETS_DIR}"
    warn "IMPORTANT: These files contain sensitive information and should never be committed to version control"
    log "To apply the secrets, run: kubectl apply -f ${SECRETS_DIR}/"
}

# Run main function with all arguments
main "$@" 