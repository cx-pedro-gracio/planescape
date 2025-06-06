#!/bin/bash

# Planescape Debug Logging Script
# This script provides organized access to debug logs for troubleshooting

set -e

NAMESPACE="planescape"
RELEASE_NAME="planescape"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_header() {
    echo -e "${BLUE}=== $1 ===${NC}"
}

print_error() {
    echo -e "${RED}ERROR: $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}WARNING: $1${NC}"
}

print_success() {
    echo -e "${GREEN}SUCCESS: $1${NC}"
}

# Function to get pod status
show_pod_status() {
    print_header "Pod Status Overview"
    kubectl get pods -n $NAMESPACE -o wide
    echo
}

# Function to show all resources
show_all_resources() {
    print_header "All Resources in Namespace"
    kubectl get all,secrets,configmaps,externalsecrets,clustersecretstores -n $NAMESPACE
    echo
}

# Function to get logs from a specific pod
get_pod_logs() {
    local pod_name=$1
    local container_name=${2:-""}
    local previous=${3:-false}
    
    echo "Getting logs for pod: $pod_name"
    
    if [ "$container_name" != "" ]; then
        echo "Container: $container_name"
    fi
    
    if [ "$previous" = true ]; then
        echo "Previous container logs:"
        kubectl logs -n $NAMESPACE $pod_name ${container_name:+-c $container_name} --previous || echo "No previous logs available"
    else
        kubectl logs -n $NAMESPACE $pod_name ${container_name:+-c $container_name} --tail=100 || echo "No logs available"
    fi
    
    echo "----------------------------------------"
}

# Function to get logs from init containers
get_init_container_logs() {
    local pod_name=$1
    
    print_header "Init Container Logs for $pod_name"
    
    # Get list of init containers
    init_containers=$(kubectl get pod -n $NAMESPACE $pod_name -o jsonpath='{.spec.initContainers[*].name}' 2>/dev/null || echo "")
    
    if [ -n "$init_containers" ]; then
        for container in $init_containers; do
            echo "=== Init Container: $container ==="
            get_pod_logs $pod_name $container
        done
    else
        echo "No init containers found for $pod_name"
    fi
    echo
}

# Function to get logs from all containers in a pod
get_all_container_logs() {
    local pod_name=$1
    
    print_header "All Container Logs for $pod_name"
    
    # Get init container logs first
    get_init_container_logs $pod_name
    
    # Get regular container logs
    containers=$(kubectl get pod -n $NAMESPACE $pod_name -o jsonpath='{.spec.containers[*].name}' 2>/dev/null || echo "")
    
    if [ -n "$containers" ]; then
        for container in $containers; do
            echo "=== Container: $container ==="
            get_pod_logs $pod_name $container
        done
    else
        echo "No containers found for $pod_name"
    fi
    echo
}

# Function to get logs from Helm hook jobs
get_job_logs() {
    print_header "Helm Hook Job Logs"
    
    jobs=$(kubectl get jobs -n $NAMESPACE -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || echo "")
    
    if [ -n "$jobs" ]; then
        for job in $jobs; do
            echo "=== Job: $job ==="
            pods=$(kubectl get pods -n $NAMESPACE -l job-name=$job -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$pods" ]; then
                for pod in $pods; do
                    echo "--- Pod: $pod ---"
                    get_pod_logs $pod
                done
            else
                echo "No pods found for job $job"
            fi
        done
    else
        echo "No jobs found"
    fi
    echo
}

# Function to get specific component logs
get_vault_logs() {
    print_header "Vault Logs"
    vault_pods=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=vault -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || echo "")
    
    for pod in $vault_pods; do
        get_all_container_logs $pod
    done
}

get_postgresql_logs() {
    print_header "PostgreSQL Logs"
    pg_pods=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || echo "")
    
    for pod in $pg_pods; do
        get_all_container_logs $pod
    done
}

get_jenkins_logs() {
    print_header "Jenkins Logs"
    jenkins_pods=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=jenkins -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || echo "")
    
    for pod in $jenkins_pods; do
        get_all_container_logs $pod
    done
}

# Function to get ESO logs
get_eso_logs() {
    print_header "External Secrets Operator Logs"
    eso_pods=$(kubectl get pods -n external-secrets-system -l app.kubernetes.io/name=external-secrets -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || echo "")
    
    for pod in $eso_pods; do
        echo "=== ESO Pod: $pod ==="
        kubectl logs -n external-secrets-system $pod --tail=100 || echo "No logs available"
        echo "----------------------------------------"
    done
    echo
}

# Function to describe problematic pods
describe_problematic_pods() {
    print_header "Describing Problematic Pods"
    
    problematic_pods=$(kubectl get pods -n $NAMESPACE --field-selector=status.phase!=Running -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || echo "")
    
    if [ -n "$problematic_pods" ]; then
        for pod in $problematic_pods; do
            echo "=== Describing Pod: $pod ==="
            kubectl describe pod -n $NAMESPACE $pod
            echo "----------------------------------------"
        done
    else
        print_success "No problematic pods found - all pods are running!"
    fi
    echo
}

# Function to get events
get_events() {
    print_header "Recent Events in Namespace"
    kubectl get events -n $NAMESPACE --sort-by='.lastTimestamp' --field-selector type=Warning || echo "No warning events"
    echo
    kubectl get events -n $NAMESPACE --sort-by='.lastTimestamp' | tail -20
    echo
}

# Function to follow logs in real-time
follow_logs() {
    local component=$1
    
    case $component in
        "vault")
            vault_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=vault -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$vault_pod" ]; then
                print_header "Following Vault logs (Ctrl+C to stop)"
                kubectl logs -n $NAMESPACE -f $vault_pod
            else
                print_error "No Vault pod found"
            fi
            ;;
        "postgresql")
            pg_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$pg_pod" ]; then
                print_header "Following PostgreSQL logs (Ctrl+C to stop)"
                kubectl logs -n $NAMESPACE -f $pg_pod
            else
                print_error "No PostgreSQL pod found"
            fi
            ;;
        "jenkins")
            jenkins_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=jenkins -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$jenkins_pod" ]; then
                print_header "Following Jenkins logs (Ctrl+C to stop)"
                kubectl logs -n $NAMESPACE -f $jenkins_pod -c jenkins
            else
                print_error "No Jenkins pod found"
            fi
            ;;
        "eso")
            eso_pod=$(kubectl get pods -n external-secrets-system -l app.kubernetes.io/name=external-secrets -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$eso_pod" ]; then
                print_header "Following ESO logs (Ctrl+C to stop)"
                kubectl logs -n external-secrets-system -f $eso_pod
            else
                print_error "No ESO pod found"
            fi
            ;;
        *)
            print_error "Unknown component: $component"
            echo "Available components: vault, postgresql, jenkins, eso"
            ;;
    esac
}

# Main menu
show_menu() {
    echo "Planescape Debug Logger"
    echo "======================"
    echo
    echo "Quick Actions:"
    echo "1. Show pod status"
    echo "2. Show all resources"
    echo "3. Get all problematic pod logs"
    echo "4. Get Helm job logs"
    echo "5. Get events"
    echo "6. Describe problematic pods"
    echo
    echo "Component Logs:"
    echo "7. Vault logs"
    echo "8. PostgreSQL logs"
    echo "9. Jenkins logs"
    echo "10. ESO logs"
    echo
    echo "Follow Logs (real-time):"
    echo "11. Follow Vault logs"
    echo "12. Follow PostgreSQL logs"
    echo "13. Follow Jenkins logs"
    echo "14. Follow ESO logs"
    echo
    echo "0. Exit"
    echo
    read -p "Select option: " choice
    
    case $choice in
        1) show_pod_status ;;
        2) show_all_resources ;;
        3) 
            describe_problematic_pods
            get_job_logs
            ;;
        4) get_job_logs ;;
        5) get_events ;;
        6) describe_problematic_pods ;;
        7) get_vault_logs ;;
        8) get_postgresql_logs ;;
        9) get_jenkins_logs ;;
        10) get_eso_logs ;;
        11) follow_logs "vault" ;;
        12) follow_logs "postgresql" ;;
        13) follow_logs "jenkins" ;;
        14) follow_logs "eso" ;;
        0) exit 0 ;;
        *) 
            print_error "Invalid option"
            show_menu
            ;;
    esac
}

# Parse command line arguments
if [ $# -eq 0 ]; then
    show_menu
else
    case $1 in
        "status") show_pod_status ;;
        "resources") show_all_resources ;;
        "events") get_events ;;
        "jobs") get_job_logs ;;
        "vault") get_vault_logs ;;
        "postgresql") get_postgresql_logs ;;
        "jenkins") get_jenkins_logs ;;
        "eso") get_eso_logs ;;
        "describe") describe_problematic_pods ;;
        "follow")
            if [ $# -eq 2 ]; then
                follow_logs $2
            else
                print_error "Usage: $0 follow <component>"
                echo "Available components: vault, postgresql, jenkins, eso"
            fi
            ;;
        "quick")
            show_pod_status
            get_events
            describe_problematic_pods
            get_job_logs
            ;;
        *)
            echo "Usage: $0 [command]"
            echo
            echo "Commands:"
            echo "  status      - Show pod status"
            echo "  resources   - Show all resources"
            echo "  events      - Show recent events"
            echo "  jobs        - Show Helm job logs"
            echo "  vault       - Show Vault logs"
            echo "  postgresql  - Show PostgreSQL logs"
            echo "  jenkins     - Show Jenkins logs"
            echo "  eso         - Show ESO logs"
            echo "  describe    - Describe problematic pods"
            echo "  follow <component> - Follow logs in real-time"
            echo "  quick       - Quick troubleshooting overview"
            echo
            echo "Run without arguments for interactive menu."
            ;;
    esac
fi 