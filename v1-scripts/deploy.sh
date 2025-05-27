#!/bin/bash

# Planescape Deployment Script
# This script deploys Planescape components using Helm charts

set -euo pipefail

# Configuration
NAMESPACE="${NAMESPACE:-planescape-system}"
ENVIRONMENT="${ENVIRONMENT:-local}"
RELEASE_NAME="${RELEASE_NAME:-planescape}"
DRY_RUN="${DRY_RUN:-false}"
WAIT_TIMEOUT="${WAIT_TIMEOUT:-300}"

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
Planescape Deployment Script

Usage: $0 [OPTIONS]

Options:
    -n, --namespace NAMESPACE       Kubernetes namespace (default: planescape-system)
    -e, --environment ENV          Environment to deploy (local, dev, prod) (default: local)
    -r, --release NAME             Helm release name (default: planescape)
    -d, --dry-run                  Show what would be deployed without applying
    -w, --wait TIMEOUT            Wait timeout in seconds (default: 300)
    -h, --help                     Show this help message

Environment Variables:
    NAMESPACE                      Same as --namespace
    ENVIRONMENT                    Same as --environment
    RELEASE_NAME                   Same as --release
    DRY_RUN                        Same as --dry-run (true/false)
    WAIT_TIMEOUT                   Same as --wait

Examples:
    # Deploy with defaults (local environment)
    $0

    # Deploy to dev environment
    $0 --environment dev

    # Deploy to prod environment with custom namespace
    $0 --environment prod --namespace my-planescape

    # Dry run to see what would be deployed
    $0 --dry-run

EOF
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--namespace)
            NAMESPACE="$2"
            shift 2
            ;;
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        -r|--release)
            RELEASE_NAME="$2"
            shift 2
            ;;
        -d|--dry-run)
            DRY_RUN="true"
            shift
            ;;
        -w|--wait)
            WAIT_TIMEOUT="$2"
            shift 2
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl is required but not installed"
        exit 1
    fi
    
    if ! command -v helm &> /dev/null; then
        log_error "helm is required but not installed"
        exit 1
    fi
    
    if ! kubectl cluster-info &> /dev/null; then
        log_error "Cannot connect to Kubernetes cluster"
        exit 1
    fi
    
    # Validate environment
    if [[ ! -f "charts/envs/${ENVIRONMENT}/values.yaml" ]]; then
        log_error "Environment '${ENVIRONMENT}' not found in charts/envs/"
        echo "Available environments:"
        ls -1 charts/envs/
        exit 1
    fi
    
    log_success "Prerequisites check passed"
}

# Add required Helm repositories
add_helm_repos() {
    log_info "Adding Helm repositories..."
    
    # Add repositories if not already added
    helm repo add hashicorp https://helm.releases.hashicorp.com || true
    helm repo add bitnami https://charts.bitnami.com/bitnami || true
    helm repo add jenkins https://charts.jenkins.io || true
    
    # Update repositories
    helm repo update
    
    log_success "Helm repositories added and updated"
}

# Create namespace if it doesn't exist
create_namespace() {
    log_info "Creating namespace: $NAMESPACE"
    
    if kubectl get namespace "$NAMESPACE" &> /dev/null; then
        log_warning "Namespace $NAMESPACE already exists"
    else
        if [[ "$DRY_RUN" == "true" ]]; then
            log_info "[DRY RUN] Would create namespace: $NAMESPACE"
        else
            kubectl create namespace "$NAMESPACE"
            log_success "Created namespace: $NAMESPACE"
        fi
    fi
}

# Deploy using Helm
deploy_helm() {
    log_info "Deploying Planescape components using Helm..."
    
    # Build Helm command
    HELM_CMD="helm upgrade --install $RELEASE_NAME charts/"
    HELM_CMD+=" --namespace $NAMESPACE"
    HELM_CMD+=" -f charts/envs/${ENVIRONMENT}/values.yaml"
    
    if [[ "$DRY_RUN" == "true" ]]; then
        HELM_CMD+=" --dry-run"
    else
        HELM_CMD+=" --wait --timeout ${WAIT_TIMEOUT}s"
    fi
    
    # Execute Helm command
    if [[ "$DRY_RUN" == "true" ]]; then
        log_info "[DRY RUN] Would execute: $HELM_CMD"
        eval "$HELM_CMD"
    else
        eval "$HELM_CMD"
        log_success "Helm deployment completed"
    fi
}

# Verify deployment
verify_deployment() {
    if [[ "$DRY_RUN" == "true" ]]; then
        log_info "[DRY RUN] Skipping verification"
        return
    fi
    
    log_info "Verifying deployment..."
    
    # Check Helm release
    if helm status "$RELEASE_NAME" -n "$NAMESPACE" &> /dev/null; then
        log_success "Helm release is installed"
    else
        log_error "Helm release is not installed"
        exit 1
    fi
    
    # Check pods
    log_info "Checking pod status..."
    if kubectl get pods -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME" | grep -q Running; then
        log_success "All pods are running"
    else
        log_error "Some pods are not running"
        kubectl get pods -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME"
        exit 1
    fi
}

# Main execution
main() {
    log_info "Starting Planescape deployment..."
    log_info "Environment: $ENVIRONMENT"
    log_info "Namespace: $NAMESPACE"
    log_info "Release name: $RELEASE_NAME"
    
    check_prerequisites
    add_helm_repos
    create_namespace
    deploy_helm
    verify_deployment
    
    log_success "Deployment completed successfully!"
    
    # Show access information
    if [[ "$DRY_RUN" == "false" ]]; then
        echo ""
        echo "Access Information:"
        echo "------------------"
        echo "Vault:"
        echo "  kubectl port-forward -n $NAMESPACE svc/${RELEASE_NAME}-vault 8200:8200"
        echo "  http://localhost:8200"
        echo ""
        echo "Jenkins:"
        echo "  kubectl port-forward -n $NAMESPACE svc/${RELEASE_NAME}-jenkins 8080:8080"
        echo "  http://localhost:8080"
        echo ""
        echo "PostgreSQL:"
        echo "  kubectl port-forward -n $NAMESPACE svc/${RELEASE_NAME}-postgresql 5432:5432"
        echo "  localhost:5432"
    fi
}

# Run main function
main 