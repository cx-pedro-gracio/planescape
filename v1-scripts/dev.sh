#!/bin/bash

# Planescape Development Script
# This script helps with local development workflow using Helm charts

set -euo pipefail

# Configuration
NAMESPACE="${NAMESPACE:-planescape-system}"
RELEASE_NAME="${RELEASE_NAME:-planescape}"
ENVIRONMENT="${ENVIRONMENT:-local}"
LOCAL_PORT_VAULT="${LOCAL_PORT_VAULT:-8200}"
LOCAL_PORT_JENKINS="${LOCAL_PORT_JENKINS:-8080}"
LOCAL_PORT_POSTGRES="${LOCAL_PORT_POSTGRES:-5432}"

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
Planescape Development Script

Usage: $0 COMMAND [OPTIONS]

Commands:
    deploy                          Deploy using Helm charts
    upgrade                         Upgrade existing deployment
    uninstall                       Uninstall deployment
    port-forward                    Set up port forwarding for services
    logs                            Show component logs
    status                          Show deployment status
    clean                           Clean up local deployment
    reset                           Reset everything (clean + deploy)

Options:
    -n, --namespace NAMESPACE       Kubernetes namespace (default: planescape-system)
    -e, --environment ENV          Environment to deploy (local, dev, prod) (default: local)
    -r, --release NAME             Helm release name (default: planescape)
    -h, --help                      Show this help message

Examples:
    # Deploy with defaults
    $0 deploy

    # Deploy to dev environment
    $0 deploy --environment dev

    # Set up port forwarding
    $0 port-forward

    # Check status
    $0 status

    # View logs
    $0 logs

    # Clean up
    $0 clean

EOF
}

# Deploy using Helm
deploy_helm() {
    log_info "Deploying Planescape components using Helm..."
    
    # Add Helm repositories if needed
    helm repo add hashicorp https://helm.releases.hashicorp.com || true
    helm repo add bitnami https://charts.bitnami.com/bitnami || true
    helm repo add jenkins https://charts.jenkins.io || true
    helm repo update
    
    # Deploy using the deploy script
    ./scripts/deploy.sh --namespace "$NAMESPACE" --environment "$ENVIRONMENT" --release "$RELEASE_NAME"
}

# Upgrade existing deployment
upgrade_helm() {
    log_info "Upgrading Planescape deployment..."
    
    # Add Helm repositories if needed
    helm repo update
    
    # Upgrade using the deploy script
    ./scripts/deploy.sh --namespace "$NAMESPACE" --environment "$ENVIRONMENT" --release "$RELEASE_NAME"
}

# Uninstall deployment
uninstall_helm() {
    log_info "Uninstalling Planescape deployment..."
    
    if helm status "$RELEASE_NAME" -n "$NAMESPACE" &> /dev/null; then
        helm uninstall "$RELEASE_NAME" -n "$NAMESPACE"
        log_success "Deployment uninstalled"
    else
        log_warning "No deployment found with release name: $RELEASE_NAME"
    fi
}

# Set up port forwarding
setup_port_forward() {
    log_info "Setting up port forwarding..."
    
    # Kill existing port-forward processes
    pkill -f "kubectl port-forward" || true
    sleep 2
    
    # Start port forwarding in background
    if kubectl get svc -n "$NAMESPACE" "${RELEASE_NAME}-vault" &> /dev/null; then
    log_info "Port forwarding Vault to localhost:$LOCAL_PORT_VAULT"
        kubectl port-forward -n "$NAMESPACE" "svc/${RELEASE_NAME}-vault" "$LOCAL_PORT_VAULT:8200" &
    fi
    
    if kubectl get svc -n "$NAMESPACE" "${RELEASE_NAME}-jenkins" &> /dev/null; then
        log_info "Port forwarding Jenkins to localhost:$LOCAL_PORT_JENKINS"
        kubectl port-forward -n "$NAMESPACE" "svc/${RELEASE_NAME}-jenkins" "$LOCAL_PORT_JENKINS:8080" &
    fi
    
    if kubectl get svc -n "$NAMESPACE" "${RELEASE_NAME}-postgresql" &> /dev/null; then
        log_info "Port forwarding PostgreSQL to localhost:$LOCAL_PORT_POSTGRES"
        kubectl port-forward -n "$NAMESPACE" "svc/${RELEASE_NAME}-postgresql" "$LOCAL_PORT_POSTGRES:5432" &
    fi
    
    sleep 3
    
    log_success "Port forwarding set up. Services available at:"
    echo "  Vault: http://localhost:$LOCAL_PORT_VAULT"
    echo "  Jenkins: http://localhost:$LOCAL_PORT_JENKINS"
    echo "  PostgreSQL: localhost:$LOCAL_PORT_POSTGRES"
    echo ""
    echo "To stop port forwarding: pkill -f 'kubectl port-forward'"
}

# Show logs
show_logs() {
    log_info "Showing component logs..."
    
    # Show logs for each component
    if kubectl get pods -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME,app.kubernetes.io/name=vault" &> /dev/null; then
        echo "=== Vault Logs ==="
        kubectl logs -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME,app.kubernetes.io/name=vault" --tail=100 -f
    fi
    
    if kubectl get pods -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME,app.kubernetes.io/name=jenkins" &> /dev/null; then
        echo "=== Jenkins Logs ==="
        kubectl logs -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME,app.kubernetes.io/name=jenkins" --tail=100 -f
    fi
    
    if kubectl get pods -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME,app.kubernetes.io/name=postgresql" &> /dev/null; then
        echo "=== PostgreSQL Logs ==="
        kubectl logs -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME,app.kubernetes.io/name=postgresql" --tail=100 -f
    fi
}

# Show status
show_status() {
    log_info "Deployment Status:"
    echo ""
    
    # Helm release status
    echo "Helm Release:"
    if helm status "$RELEASE_NAME" -n "$NAMESPACE" &> /dev/null; then
        helm status "$RELEASE_NAME" -n "$NAMESPACE"
    else
        echo "  Not installed"
    fi
    echo ""
    
    # Pod status
    echo "Pods:"
    kubectl get pods -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME"
    echo ""
    
    # Services
    echo "Services:"
    kubectl get svc -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME"
    echo ""
    
    # Persistent volumes
    echo "Persistent Volumes:"
    kubectl get pvc -n "$NAMESPACE" -l "app.kubernetes.io/instance=$RELEASE_NAME"
}

# Clean up
clean_up() {
    log_info "Cleaning up local deployment..."
    
    # Stop port forwarding
    pkill -f "kubectl port-forward" || true
    
    # Uninstall Helm release
    uninstall_helm
    
    # Delete namespace if empty
    if kubectl get namespace "$NAMESPACE" &> /dev/null; then
        if ! kubectl get all -n "$NAMESPACE" 2>/dev/null | grep -q .; then
            log_info "Deleting empty namespace: $NAMESPACE"
            kubectl delete namespace "$NAMESPACE"
        else
            log_warning "Namespace $NAMESPACE is not empty, skipping deletion"
        fi
    fi
    
    log_success "Cleanup completed"
}

# Reset everything
reset_deployment() {
    log_info "Resetting deployment..."
    clean_up
    deploy_helm
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
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            COMMAND="$1"
            shift
            break
            ;;
    esac
done

# Execute command
case "${COMMAND:-}" in
    deploy)
        deploy_helm
        ;;
    upgrade)
        upgrade_helm
        ;;
    uninstall)
        uninstall_helm
        ;;
    port-forward)
        setup_port_forward
        ;;
    logs)
        show_logs
        ;;
    status)
        show_status
        ;;
    clean)
        clean_up
        ;;
    reset)
        reset_deployment
        ;;
    *)
        log_error "Unknown command: ${COMMAND:-}"
        show_help
        exit 1
        ;;
esac 