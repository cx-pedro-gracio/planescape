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

# Global variable for log follow mode
FOLLOW_LOGS=false

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
        kubectl logs -n $NAMESPACE $pod_name ${container_name:+-c $container_name} --previous --follow=$FOLLOW_LOGS || echo "No previous logs available"
    else
        kubectl logs -n $NAMESPACE $pod_name ${container_name:+-c $container_name} --tail=100 --follow=$FOLLOW_LOGS || echo "No logs available"
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
        kubectl logs -n external-secrets-system $pod --tail=100 --follow=$FOLLOW_LOGS || echo "No logs available"
        echo "----------------------------------------"
    done
    echo
}

# Function to list PostgreSQL tables and their row counts
get_postgres_tables() {
    print_header "PostgreSQL Database Tables"
    local timeout_sec=10
    pg_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
    if [ -z "$pg_pod" ]; then
        print_error "No PostgreSQL pod found"
        return 1
    fi
    print_success "Connected to PostgreSQL pod: $pg_pod"
    echo
    # Get database credentials from Vault-generated secret
    vault_password=$(kubectl get secret -n $NAMESPACE ${RELEASE_NAME}-postgres-admin -o jsonpath='{.data.password}' 2>/dev/null | base64 -d || echo "")
    vault_username=$(kubectl get secret -n $NAMESPACE ${RELEASE_NAME}-postgres-admin -o jsonpath='{.data.username}' 2>/dev/null | base64 -d || echo "postgres-admin")
    # Also try getting from traditional PostgreSQL secret
    pg_password=$(kubectl get secret -n $NAMESPACE ${RELEASE_NAME}-postgresql -o jsonpath='{.data.postgres-password}' 2>/dev/null | base64 -d || echo "")
    # Function to execute SQL with credentials (with timeout)
    execute_sql() {
        local sql_query="$1"
        local username="$2"
        local password="$3"
        if [ -n "$password" ] && [ -n "$username" ]; then
            timeout $timeout_sec kubectl exec -n $NAMESPACE $pg_pod -- env PGPASSWORD="$password" psql -U "$username" -d planescape -c "$sql_query" 2>/dev/null
        else
            timeout $timeout_sec kubectl exec -n $NAMESPACE $pg_pod -- psql -U postgres -d planescape -c "$sql_query" 2>/dev/null
        fi
    }
    # Function to list databases with credentials (with timeout)
    list_databases() {
        local username="$1"
        local password="$2"
        if [ -n "$password" ] && [ -n "$username" ]; then
            timeout $timeout_sec kubectl exec -n $NAMESPACE $pg_pod -- env PGPASSWORD="$password" psql -U "$username" -l 2>/dev/null
        else
            timeout $timeout_sec kubectl exec -n $NAMESPACE $pg_pod -- psql -U postgres -l 2>/dev/null
        fi
    }
    # Try Vault credentials first
    if [ -n "$vault_password" ]; then
        print_success "Using Vault-generated credentials (user: $vault_username)"
        echo "=== Available Databases ==="
        if list_databases "$vault_username" "$vault_password"; then
            connection_works=true
            working_user="$vault_username"
            working_password="$vault_password"
        else
            print_warning "Vault credentials failed, trying postgres user with Vault password..."
            if list_databases "postgres" "$vault_password"; then
                connection_works=true
                working_user="postgres"
                working_password="$vault_password"
            else
                connection_works=false
            fi
        fi
    elif [ -n "$pg_password" ]; then
        print_success "Using traditional PostgreSQL secret"
        echo "=== Available Databases ==="
        if list_databases "postgres" "$pg_password"; then
            connection_works=true
            working_user="postgres"
            working_password="$pg_password"
        else
            connection_works=false
        fi
    else
        print_warning "No database credentials found, trying passwordless connection..."
        echo "=== Available Databases ==="
        if list_databases "" ""; then
            connection_works=true
            working_user="postgres"
            working_password=""
        else
            connection_works=false
        fi
    fi
    if [ "$connection_works" != true ]; then
        print_error "Failed to connect to PostgreSQL with any credentials"
        echo "Available secrets:"
        kubectl get secrets -n $NAMESPACE | grep postgres
        echo
        return 1
    fi
    echo
    echo "=== Tables in 'planescape' database ==="
    # Check if planescape database exists and list tables
    if execute_sql "\dt" "$working_user" "$working_password" >/dev/null 2>&1; then
        echo "Tables in planescape database:"
        execute_sql "\dt" "$working_user" "$working_password"
        echo
        echo "=== Table Row Counts ==="
        # Get list of tables
        tables=$(execute_sql "SELECT tablename FROM pg_tables WHERE schemaname = 'public';" "$working_user" "$working_password" | grep -v tablename | grep -v "^-" | grep -v "row" | grep -v "^$" | tr -d ' ')
        if [ -n "$tables" ]; then
            for table in $tables; do
                if [ -n "$table" ] && [ "$table" != "tablename" ]; then
                    count=$(execute_sql "SELECT COUNT(*) FROM $table;" "$working_user" "$working_password" | grep -E "^[0-9]+$" | head -1)
                    if [ -n "$count" ]; then
                        printf "%-20s: %s rows\n" "$table" "$count"
                    fi
                fi
            done
        else
            echo "No tables found in planescape database"
        fi
        echo
        echo "=== Sample Data from 'logs' table (if exists) ==="
        if execute_sql "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'logs');" "$working_user" "$working_password" | grep -q "t"; then
            echo "Recent entries from logs table:"
            execute_sql "SELECT * FROM logs ORDER BY timestamp DESC LIMIT 5;" "$working_user" "$working_password"
        else
            echo "No 'logs' table found"
            echo "Creating logs table for Jenkins..."
            execute_sql "CREATE TABLE IF NOT EXISTS logs (id SERIAL PRIMARY KEY, message TEXT, timestamp BIGINT);" "$working_user" "$working_password"
            if [ $? -eq 0 ]; then
                print_success "Created logs table successfully"
            else
                print_warning "Failed to create logs table"
            fi
        fi
    else
        print_warning "Could not connect to 'planescape' database"
        echo "Trying to create planescape database..."
        if [ -n "$working_password" ]; then
            timeout $timeout_sec kubectl exec -n $NAMESPACE $pg_pod -- env PGPASSWORD="$working_password" psql -U "$working_user" -c "CREATE DATABASE planescape;" 2>/dev/null && print_success "Created planescape database"
        else
            timeout $timeout_sec kubectl exec -n $NAMESPACE $pg_pod -- psql -U "$working_user" -c "CREATE DATABASE planescape;" 2>/dev/null && print_success "Created planescape database"
        fi
        echo "Available databases:"
        list_databases "$working_user" "$working_password"
    fi
    echo
    return 0
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
                kubectl logs -n $NAMESPACE -f $vault_pod --follow=$FOLLOW_LOGS
            else
                print_error "No Vault pod found"
            fi
            ;;
        "postgresql")
            pg_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$pg_pod" ]; then
                print_header "Following PostgreSQL logs (Ctrl+C to stop)"
                kubectl logs -n $NAMESPACE -f $pg_pod --follow=$FOLLOW_LOGS
            else
                print_error "No PostgreSQL pod found"
            fi
            ;;
        "jenkins")
            jenkins_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=jenkins -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$jenkins_pod" ]; then
                print_header "Following Jenkins logs (Ctrl+C to stop)"
                kubectl logs -n $NAMESPACE -f $jenkins_pod -c jenkins --follow=$FOLLOW_LOGS
            else
                print_error "No Jenkins pod found"
            fi
            ;;
        "eso")
            eso_pod=$(kubectl get pods -n external-secrets-system -l app.kubernetes.io/name=external-secrets -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
            if [ -n "$eso_pod" ]; then
                print_header "Following ESO logs (Ctrl+C to stop)"
                kubectl logs -n external-secrets-system -f $eso_pod --follow=$FOLLOW_LOGS
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

# Function to handle orphaned PVCs (PVCs that exist but their namespace is gone)
handle_orphaned_pvcs() {
    print_header "Handling Orphaned PVCs"
    orphaned_pvcs=$(kubectl get pvc --all-namespaces 2>/dev/null | grep -E "(planescape|external-secrets)" || echo "")
    if [ -z "$orphaned_pvcs" ]; then
        print_success "No orphaned PVCs found."
        return
    fi
    
    echo "$orphaned_pvcs" | tail -n +2 | while read ns pvc status rest; do
        echo "Found PVC $pvc in namespace $ns"
        # Create namespace temporarily if it doesn't exist
        if ! kubectl get ns "$ns" &>/dev/null; then
            echo "Creating temporary namespace $ns to handle orphaned PVC..."
            kubectl create namespace "$ns" || echo "Failed to create namespace $ns"
        fi
        
        # Remove finalizers from the PVC
        echo "Removing finalizers from PVC $pvc in namespace $ns..."
        kubectl patch pvc "$pvc" -n "$ns" -p '{"metadata":{"finalizers":[]}}' --type=merge || echo "Failed to patch PVC $pvc"
        
        # Delete the namespace (which should clean up the PVC)
        echo "Deleting namespace $ns..."
        kubectl delete namespace "$ns" --grace-period=0 --force || echo "Failed to delete namespace $ns"
    done
    print_success "Orphaned PVC cleanup attempted."
}

# Enhanced function to force delete stuck pods with multiple strategies
force_delete_stuck_pods_aggressive() {
    print_header "Aggressively Force Deleting Stuck Pods"
    # Get all pods that might be stuck (Terminating, Pending with long age, etc.)
    all_pods=$(kubectl get pods --all-namespaces -o custom-columns=NS:metadata.namespace,NAME:metadata.name,STATUS:status.phase,AGE:metadata.creationTimestamp --no-headers 2>/dev/null | grep -E "(planescape|external-secrets)")
    
    if [ -z "$all_pods" ]; then
        print_success "No pods found for aggressive cleanup."
        return
    fi
    
    echo "$all_pods" | while read ns pod status age; do
        echo "Force deleting pod $pod in namespace $ns (status: $status)..."
        # First remove finalizers
        kubectl patch pod "$pod" -n "$ns" -p '{"metadata":{"finalizers":[]}}' --type=merge 2>/dev/null || echo "Could not patch finalizers for $pod"
        # Then force delete
        kubectl delete pod "$pod" -n "$ns" --grace-period=0 --force 2>/dev/null || echo "Could not force delete $pod"
    done
    print_success "Aggressive pod cleanup completed."
}

# Enhanced function to force delete stuck PVCs with multiple strategies  
force_delete_stuck_pvcs_aggressive() {
    print_header "Aggressively Force Deleting Stuck PVCs"
    stuck_pvcs=$(kubectl get pvc --all-namespaces 2>/dev/null | grep -E "(planescape|external-secrets)" || echo "")
    
    if [ -z "$stuck_pvcs" ]; then
        print_success "No PVCs found for aggressive cleanup."
        return
    fi
    
    echo "$stuck_pvcs" | tail -n +2 | while read ns pvc status rest; do
        echo "Force deleting PVC $pvc in namespace $ns..."
        # First try to remove finalizers
        kubectl patch pvc "$pvc" -n "$ns" -p '{"metadata":{"finalizers":[]}}' --type=merge 2>/dev/null || echo "Could not patch finalizers for PVC $pvc"
        # Then force delete
        kubectl delete pvc "$pvc" -n "$ns" --grace-period=0 --force 2>/dev/null || echo "Could not force delete PVC $pvc"
    done
    print_success "Aggressive PVC cleanup completed."
}

# Enhanced function to handle stuck namespaces with multiple strategies
force_delete_namespaces_aggressive() {
    print_header "Aggressively Deleting Stuck Namespaces"
    for ns in planescape external-secrets-system; do
        if kubectl get ns "$ns" &>/dev/null; then
            echo "Aggressively deleting namespace $ns..."
            
            # Strategy 1: Remove finalizers and force delete
            kubectl get ns "$ns" -o json | jq 'del(.spec.finalizers)' | kubectl replace --raw "/api/v1/namespaces/$ns/finalize" -f - 2>/dev/null || echo "Could not patch finalizers for namespace $ns"
            
            # Strategy 2: Force delete with zero grace period
            kubectl delete namespace "$ns" --grace-period=0 --force 2>/dev/null || echo "Could not force delete namespace $ns"
            
            # Strategy 3: If still exists, try patching the metadata
            if kubectl get ns "$ns" &>/dev/null; then
                kubectl patch namespace "$ns" -p '{"metadata":{"finalizers":[]}}' --type=merge 2>/dev/null || echo "Could not patch namespace metadata for $ns"
                kubectl delete namespace "$ns" --grace-period=0 --force 2>/dev/null || echo "Final delete attempt failed for namespace $ns"
            fi
        fi
    done
    print_success "Aggressive namespace cleanup completed."
}

# Function to perform a full cleanup/reset of Planescape and ESO
full_cleanup() {
    print_header "Full Cluster Cleanup: Planescape & ESO (AGGRESSIVE MODE)"
    echo "Uninstalling Helm releases..."
    timeout 60s helm uninstall planescape -n planescape || echo "Helm uninstall planescape timed out or failed"
    timeout 60s helm uninstall external-secrets -n external-secrets-system || echo "Helm uninstall external-secrets timed out or failed"
    echo
    # Aggressively finalize all resources before deleting namespaces
    echo "Aggressively finalizing all resources before deleting namespaces..."
    cleanup_all_finalizers
    echo
    # Always delete the Vault secret for a clean slate
    echo "Deleting Vault secret: secret/planescape/postgres (if exists)..."
    kubectl delete secret planescape-postgres-admin -n planescape --ignore-not-found
    kubectl delete externalsecret planescape-postgres-admin -n planescape --ignore-not-found
    kubectl exec -n planescape $(kubectl get pods -n planescape -l app.kubernetes.io/name=vault -o jsonpath='{.items[0].metadata.name}' 2>/dev/null) -- sh -c 'vault kv delete secret/planescape/postgres' 2>/dev/null || echo "Could not delete Vault secret via pod (may not exist or pod not running)"
    echo "Deleting PostgreSQL PVCs..."
    kubectl delete pvc -n planescape -l app.kubernetes.io/name=postgresql --ignore-not-found
    echo
    # Wait for all resources in planescape and external-secrets-system to be deleted (with a timeout)
    for ns in planescape external-secrets-system; do
        echo "Waiting for all resources in namespace $ns to be deleted before deleting the namespace..."
        for i in {1..3}; do
            remaining=$(kubectl get all -n $ns --no-headers 2>/dev/null | wc -l)
            if [ "$remaining" -eq 0 ]; then
                echo "All resources in $ns deleted."
                break
            fi
            echo "Still $remaining resources in $ns. Waiting... ($i/3)"
            sleep 2
        done
        # After waiting, check if resources are still present
        remaining=$(kubectl get all -n $ns --no-headers 2>/dev/null | wc -l)
        if [ "$remaining" -ne 0 ]; then
            print_warning "Some resources in $ns are still present after waiting. Proceeding with namespace deletion, but orphaned resources may remain."
        fi
    done
    echo
    echo "Deleting namespaces (if present)..."
    timeout 60s kubectl delete ns planescape --wait=false || echo "Namespace planescape delete timed out or failed"
    timeout 60s kubectl delete ns external-secrets-system --wait=false || echo "Namespace external-secrets-system delete timed out or failed"
    echo
    echo "Removing stuck finalizers (if any)..."
    for ns in planescape external-secrets-system; do
        if kubectl get ns $ns &>/dev/null; then
            timeout 30s kubectl get ns $ns -o json | jq 'if .spec.finalizers then .spec.finalizers=[] else . end' | kubectl replace --raw "/api/v1/namespaces/$ns/finalize" -f - || echo "Finalizer removal for $ns timed out or failed"
        fi
    done
    echo
    echo "Cleaning up PVCs and PVs..."
    timeout 30s kubectl get pvc -A | grep planescape && timeout 30s kubectl delete pvc --all -n planescape || echo "PVC cleanup timed out or failed"
    timeout 30s kubectl get pv | grep Released | awk '{print $1}' | xargs -I{} timeout 10s kubectl delete pv {} || echo "PV cleanup timed out or failed"
    echo
    # Wait for all PVCs and PVs to be deleted before deleting the namespace
    for ns in planescape external-secrets-system; do
        echo "Waiting for all PVCs and PVs in namespace $ns to be deleted before deleting the namespace..."
        for i in {1..3}; do
            pvc_count=$(kubectl get pvc -n $ns --no-headers 2>/dev/null | wc -l)
            pv_count=$(kubectl get pv --no-headers 2>/dev/null | grep $ns | wc -l)
            if [ "$pvc_count" -eq 0 ] && [ "$pv_count" -eq 0 ]; then
                echo "All PVCs and PVs in $ns deleted."
                break
            fi
            echo "Waiting for PVCs ($pvc_count) and PVs ($pv_count) to be deleted in $ns... ($i/3)"
            sleep 2
        done
        # After waiting, check if PVCs or PVs are still present
        pvc_count=$(kubectl get pvc -n $ns --no-headers 2>/dev/null | wc -l)
        pv_count=$(kubectl get pv --no-headers 2>/dev/null | grep $ns | wc -l)
        if [ "$pvc_count" -ne 0 ] || [ "$pv_count" -ne 0 ]; then
            print_warning "Some PVCs or PVs in $ns are still present after waiting. Proceeding with namespace deletion, but orphaned resources may remain."
        fi
    done
    # After namespace deletion, optionally recreate and run finalizer cleanup if any PVCs/PVs are still stuck
    for ns in planescape external-secrets-system; do
        stuck_pvcs=$(kubectl get pvc -n $ns --no-headers 2>/dev/null | wc -l)
        stuck_pvs=$(kubectl get pv --no-headers 2>/dev/null | grep $ns | wc -l)
        if [ "$stuck_pvcs" -ne 0 ] || [ "$stuck_pvs" -ne 0 ]; then
            echo "Recreating namespace $ns to finalize orphaned PVCs/PVs..."
            kubectl create namespace $ns || true
            cleanup_all_finalizers
        fi
    done
    echo
    echo "Cleaning up finalizers (if any)..."
    cleanup_all_finalizers
    echo
    echo "Checking for remaining terminating resources after namespace deletion..."
    sleep 5
    terminating_count=$(kubectl get pods --all-namespaces --field-selector=status.phase=Terminating -o name | wc -l)
    terminating_count=$((terminating_count + $(kubectl get pvc --all-namespaces --field-selector=status.phase=Terminating -o name | wc -l)))
    terminating_count=$((terminating_count + $(kubectl get pv --field-selector=status.phase=Terminating -o name | wc -l)))
    terminating_count=$((terminating_count + $(kubectl get statefulset --all-namespaces -o json | jq '[.items[] | select(.metadata.finalizers != null and (.metadata.finalizers | length > 0))] | length')))
    terminating_count=$((terminating_count + $(kubectl get crd -o json | jq '[.items[] | select(.metadata.finalizers != null and (.metadata.finalizers | length > 0))] | length')))
    terminating_count=$((terminating_count + $(kubectl get ns -o json | jq '[.items[] | select(.spec.finalizers != null and (.spec.finalizers | length > 0))] | length')))
    if [ "$terminating_count" -gt 0 ]; then
        print_warning "Some resources are still stuck in Terminating or have finalizers. Running aggressive finalizer cleanup again..."
        cleanup_all_finalizers
    fi
    # Final verification
    echo
    print_header "Final Cleanup Verification"
    remaining=$(kubectl get all --all-namespaces 2>/dev/null | grep -E "(planescape|external-secrets)" || echo "")
    if [ -z "$remaining" ]; then
        print_success "✅ Cleanup complete! No planescape/external-secrets resources remain."
    else
        print_warning "⚠️  Some resources may still remain:"
        echo "$remaining"
    fi
    remaining_pvcs=$(kubectl get pvc --all-namespaces 2>/dev/null | grep -E "(planescape|external-secrets)" || echo "")
    if [ -z "$remaining_pvcs" ]; then
        print_success "✅ No remaining PVCs found."
    else
        print_warning "⚠️  Some PVCs may still remain:"
        echo "$remaining_pvcs"
    fi
    kubectl delete ns planescape --wait=false
}

# Function to perform a full install of ESO and Planescape
full_install() {
    local verbose=${1:-false}
    print_header "Full Install: ESO and Planescape"
    echo "Installing External Secrets Operator (ESO)..."
    helm upgrade --install external-secrets charts/external-secrets -n external-secrets-system --create-namespace --wait || {
        print_error "ESO install failed!"
        return 1
    }
    if [ "$verbose" = true ]; then
        echo
        print_header "[Verbose] Pod Status: external-secrets-system"
        kubectl get pods -n external-secrets-system
        print_header "[Verbose] Warning Events: external-secrets-system"
        kubectl get events -n external-secrets-system --sort-by='.lastTimestamp' --field-selector type=Warning | tail -10
        print_header "[Verbose] Describing Problematic Pods: external-secrets-system"
        kubectl get pods -n external-secrets-system --field-selector=status.phase!=Running -o jsonpath='{.items[*].metadata.name}' | xargs -r -n1 -I{} kubectl describe pod -n external-secrets-system {} || true
    fi
    echo
    echo "Cleaning up any leftover Helm release secrets for planescape..."
    for s in $(kubectl get secrets -n planescape -o name | grep 'sh.helm.release.v1.planescape' || true); do
        echo "Deleting $s ..."
        kubectl delete $s -n planescape || true
    done
    echo "Installing Planescape platform..."
    helm upgrade --install planescape charts/planescape -n planescape --create-namespace --wait || {
        print_error "Planescape install failed!"
        return 1
    }
    if [ "$verbose" = true ]; then
        echo
        print_header "[Verbose] Pod Status: planescape"
        kubectl get pods -n planescape
        print_header "[Verbose] Warning Events: planescape"
        kubectl get events -n planescape --sort-by='.lastTimestamp' --field-selector type=Warning | tail -10
        print_header "[Verbose] Describing Problematic Pods: planescape"
        kubectl get pods -n planescape --field-selector=status.phase!=Running -o jsonpath='{.items[*].metadata.name}' | xargs -r -n1 -I{} kubectl describe pod -n planescape {} || true
    fi
    echo
    echo "Checking pod status in both namespaces..."
    kubectl get pods -n external-secrets-system
    kubectl get pods -n planescape
    print_success "Full install complete."
    if [ "$verbose" = true ]; then
        echo
        echo "Entering live watch mode. Press 'l' then Enter to follow logs, 'q' then Enter to exit, or Enter to refresh."
        while true; do
            clear
            print_header "[Watch] Pod Status: external-secrets-system"
            kubectl get pods -n external-secrets-system
            print_header "[Watch] Pod Status: planescape"
            kubectl get pods -n planescape
            print_header "[Watch] Warning Events: external-secrets-system"
            kubectl get events -n external-secrets-system --sort-by='.lastTimestamp' --field-selector type=Warning | tail -10
            print_header "[Watch] Warning Events: planescape"
            kubectl get events -n planescape --sort-by='.lastTimestamp' --field-selector type=Warning | tail -10
            echo
            echo "Press 'l' then Enter to follow logs for a component, 'q' then Enter to exit watch, or Enter to refresh."
            read -t 5 -r input
            if [ "$input" = "q" ]; then
                echo "Exiting watch mode."
                break
            elif [ "$input" = "l" ]; then
                echo "Available components: vault, postgresql, jenkins, eso"
                echo -n "Enter component to follow logs: "
                read component
                if [[ "$component" =~ ^(vault|postgresql|jenkins|eso)$ ]]; then
                    echo "Following logs for $component (Ctrl+C to return to watch)..."
                    follow_logs "$component"
                    echo "Returned from log follow."
                else
                    echo "Invalid component. Returning to watch."
                fi
            fi
        done
    fi
}

# Function to prompt user for log follow mode
prompt_follow_logs() {
    echo -n "Do you want to follow logs in real-time? (y/n) [n]: "
    read ans
    case $ans in
        y|Y) FOLLOW_LOGS=true ;;
        n|N|"") FOLLOW_LOGS=false ;;
        *) FOLLOW_LOGS=false ;;
    esac
    echo "Log follow mode is now: $FOLLOW_LOGS"
}

# Function to toggle log follow mode
toggle_follow_logs() {
    if [ "$FOLLOW_LOGS" = true ]; then
        FOLLOW_LOGS=false
    else
        FOLLOW_LOGS=true
    fi
    echo "Log follow mode is now: $FOLLOW_LOGS"
}

# Helper: Get Jenkins admin credentials from secret
get_jenkins_admin_creds() {
    JENKINS_USER=$(kubectl get secret planescape-jenkins-admin -n planescape -o jsonpath='{.data.jenkins-admin-user}' | base64 -d)
    JENKINS_PASS=$(kubectl get secret planescape-jenkins-admin -n planescape -o jsonpath='{.data.jenkins-admin-password}' | base64 -d)
}

# Helper: Port-forward Jenkins and curl API (with error handling)
jenkins_api_request() {
    local api_path="$1"
    local method="${2:-GET}"
    get_jenkins_admin_creds
    # Start port-forward in background
    kubectl port-forward svc/planescape-jenkins 18080:8080 -n planescape >/dev/null 2>&1 &
    PF_PID=$!
    # Wait for port-forward to be ready
    for i in {1..10}; do
        curl -s http://localhost:18080/login >/dev/null 2>&1 && break
        sleep 1
    done
    # Make API request
    local response
    response=$(curl -s -w "HTTPSTATUS:%{http_code}" -u "$JENKINS_USER:$JENKINS_PASS" -X "$method" "http://localhost:18080$api_path")
    kill $PF_PID >/dev/null 2>&1
    local body=$(echo "$response" | sed -e 's/HTTPSTATUS:.*//g')
    local status=$(echo "$response" | tr -d '\n' | sed -e 's/.*HTTPSTATUS://')
    if [ "$status" != "200" ]; then
        print_error "Jenkins API error ($status) for $api_path: $body"
        return 1
    fi
    echo "$body"
}

# List Jenkins jobs (with error handling)
list_jenkins_jobs() {
    print_header "Jenkins Jobs"
    local jobs_json=$(jenkins_api_request "/api/json?tree=jobs[name]" GET)
    if [ $? -ne 0 ] || [ -z "$jobs_json" ]; then
        print_error "Failed to fetch Jenkins jobs."
        return 1
    fi
    echo "$jobs_json" | jq -r '.jobs[].name' 2>/dev/null || print_error "No jobs found or Jenkins not ready."
}

# Show last build status for all jobs (with error handling)
show_jenkins_job_status() {
    print_header "Jenkins Job Last Build Status"
    local jobs_json=$(jenkins_api_request "/api/json?tree=jobs[name,url]" GET)
    if [ $? -ne 0 ] || [ -z "$jobs_json" ]; then
        print_error "Failed to fetch Jenkins jobs."
        return 1
    fi
    local jobs=$(echo "$jobs_json" | jq -r '.jobs[].name' 2>/dev/null)
    if [ -z "$jobs" ]; then
        print_error "No jobs found or Jenkins not ready."
        return 1
    fi
    for job in $jobs; do
        local build_json=$(jenkins_api_request "/job/$job/lastBuild/api/json" GET)
        if [ $? -ne 0 ] || [ -z "$build_json" ]; then
            print_error "Failed to fetch last build for job $job."
            continue
        fi
        local result=$(echo "$build_json" | jq -r '.result // "N/A"')
        local url=$(echo "$build_json" | jq -r '.url // "N/A"')
        printf "%s: %s (%s)\n" "$job" "$result" "$url"
    done
}

# Show console output for a specific job (with error handling)
show_jenkins_console_log() {
    print_header "Jenkins Job Console Output"
    echo -n "Enter job name: "
    read job
    local build_json=$(jenkins_api_request "/job/$job/lastBuild/api/json" GET)
    if [ $? -ne 0 ] || [ -z "$build_json" ]; then
        print_error "Failed to fetch last build for job $job."
        return 1
    fi
    local build_num=$(echo "$build_json" | jq -r '.number')
    if [ "$build_num" = "null" ] || [ -z "$build_num" ]; then
        print_error "No builds found for job $job."
        return 1
    fi
    echo "Showing console output for $job #$build_num"
    local console_out=$(jenkins_api_request "/job/$job/$build_num/consoleText" GET)
    if [ $? -ne 0 ] || [ -z "$console_out" ]; then
        print_error "Failed to fetch console output for $job #$build_num."
        return 1
    fi
    echo "$console_out"
}

# Function to debug Jenkins SeedJob (Job DSL execution/logs)
debug_jenkins_seedjob() {
    print_header "Debug Jenkins SeedJob (Job DSL Execution)"
    local build_json=$(jenkins_api_request "/job/SeedJob/lastBuild/api/json" GET)
    if [ $? -ne 0 ] || [ -z "$build_json" ]; then
        print_error "Failed to fetch last build for SeedJob."
        return 1
    fi
    local build_num=$(echo "$build_json" | jq -r '.number')
    if [ "$build_num" = "null" ] || [ -z "$build_num" ]; then
        print_error "No builds found for SeedJob."
        return 1
    fi
    echo "Showing console output for SeedJob #$build_num"
    local console_out=$(jenkins_api_request "/job/SeedJob/$build_num/consoleText" GET)
    if [ $? -ne 0 ] || [ -z "$console_out" ]; then
        print_error "Failed to fetch console output for SeedJob #$build_num."
        return 1
    fi
    echo "$console_out"
    echo
    if echo "$console_out" | grep -q "plugin 'build-user-vars-plugin' needs to be installed"; then
        print_error "Missing plugin: build-user-vars-plugin. Add it to your Jenkins installPlugins list."
    fi
    if echo "$console_out" | grep -q "No signature of method: javaposse.jobdsl.dsl.helpers.wrapper.WrapperContext.hashicorpVaultBuildWrapper"; then
        print_error "Job DSL error: hashicorpVaultBuildWrapper() is not supported in Job DSL. Use a pipeline job with withVault instead."
    fi
    if echo "$console_out" | grep -q "ERROR"; then
        print_error "See above for errors in the SeedJob DSL script."
    fi
}

# Function to clean up finalizers of terminating pods in all namespaces
cleanup_pod_finalizers() {
    print_header "Cleaning Up Finalizers of Terminating Pods (All Namespaces)"
    terminating_pods=$(kubectl get pods --all-namespaces --field-selector=status.phase=Terminating -o custom-columns=NS:metadata.namespace,NAME:metadata.name --no-headers 2>/dev/null)
    if [ -z "$terminating_pods" ]; then
        print_success "No terminating pods found."
        return
    fi
    echo "$terminating_pods" | while read ns pod; do
        if [ -n "$ns" ] && [ -n "$pod" ]; then
            echo "Removing finalizers from pod $pod in namespace $ns..."
            kubectl patch pod "$pod" -n "$ns" -p '{"metadata":{"finalizers":[]}}' --type=merge 2>/dev/null && \
                echo "Finalizers removed from $pod in $ns" || \
                echo "Failed to patch $pod in $ns"
        fi
    done
    print_success "Finalizer cleanup attempted for all terminating pods."
}

# Function to aggressively clean up finalizers from all stuck resources
cleanup_all_finalizers() {
    print_header "Aggressive Finalizer Cleanup: Pods, PVCs, PVs, StatefulSets, Namespaces, CRDs"

    # Helper to patch finalizers for a resource type
    patch_finalizers() {
        local type="$1"
        local extra_args="$2"
        local name_field="$3"
        local ns_field="$4"
        local jq_filter='.items[] | select(.metadata.finalizers != null and (.metadata.finalizers | length > 0))'
        local resources
        if [ -n "$ns_field" ]; then
            resources=$(kubectl get $type --all-namespaces $extra_args -o json | jq -r "$jq_filter | [.metadata.namespace, .metadata.name] | @tsv")
        else
            resources=$(kubectl get $type $extra_args -o json | jq -r "$jq_filter | .metadata.name")
        fi
        if [ -z "$resources" ]; then
            print_success "No $type with finalizers found."
            return
        fi
        echo "$resources" | while read ns name; do
            if [ -n "$name" ]; then
                if [ -n "$ns_field" ]; then
                    echo "Removing finalizers from $type $name in namespace $ns..."
                    kubectl patch $type "$name" -n "$ns" -p '{"metadata":{"finalizers":[]}}' --type=merge 2>/dev/null && \
                        echo "Finalizers removed from $type $name in $ns" || \
                        echo "Failed to patch $type $name in $ns"
                else
                    echo "Removing finalizers from $type $name..."
                    kubectl patch $type "$name" -p '{"metadata":{"finalizers":[]}}' --type=merge 2>/dev/null && \
                        echo "Finalizers removed from $type $name" || \
                        echo "Failed to patch $type $name"
                fi
            fi
        done
        print_success "Finalizer cleanup attempted for $type."
    }

    patch_finalizers "pod" "" ".metadata.name" ".metadata.namespace"
    patch_finalizers "pvc" "" ".metadata.name" ".metadata.namespace"
    patch_finalizers "pv" "" ".metadata.name" ""
    patch_finalizers "statefulset" "" ".metadata.name" ".metadata.namespace"
    patch_finalizers "namespace" "" ".metadata.name" ""
    patch_finalizers "crd" "" ".metadata.name" ""

    print_success "Aggressive finalizer cleanup completed for all resource types."
}

validate_groovy_jobdsl() {
    print_header "Groovy Job DSL Script Validation"
    local configmap_file="charts/planescape/templates/jenkins-jobdsl-configmap.yaml"
    local script_start_line=$(grep -n '^  log-to-postgres.groovy: |' "$configmap_file" | cut -d: -f1)
    if [ -z "$script_start_line" ]; then
        print_error "Could not find log-to-postgres.groovy in $configmap_file"
        return 1
    fi
    local script_lines=$(tail -n +$((script_start_line + 1)) "$configmap_file" | sed -n '/^[^ ]/q;p')
    if [ -z "$script_lines" ]; then
        print_error "Could not extract Groovy script from $configmap_file"
        return 1
    fi
    # Remove leading YAML indentation (4 spaces)
    local groovy_script=$(echo "$script_lines" | sed 's/^    //')
    local tmpfile=$(mktemp /tmp/planescape-jobdsl-XXXXXX.groovy)
    echo "$groovy_script" > "$tmpfile"
    echo "Groovy version: $(groovy --version | head -1)"
    echo "Validating script at $tmpfile..."
    groovy -c "$tmpfile"
    local result=$?
    if [ $result -eq 0 ]; then
        print_success "Groovy script syntax is valid."
    else
        print_error "Groovy script has syntax errors. See above."
    fi
    rm -f "$tmpfile"
    echo
}

# Function to port-forward PostgreSQL
port_forward_postgresql() {
    print_header "Port-forward PostgreSQL"
    local pg_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
    if [ -z "$pg_pod" ]; then
        print_error "No PostgreSQL pod found."
        return
    fi
    echo "Port-forwarding local port 5432 to $pg_pod:5432 in namespace $NAMESPACE..."
    echo "Use Ctrl+C to stop port-forwarding."
    kubectl port-forward -n $NAMESPACE $pg_pod 5432:5432
}

# Function to port-forward Vault
port_forward_vault() {
    print_header "Port-forward Vault"
    local vault_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=vault -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
    if [ -z "$vault_pod" ]; then
        print_error "No Vault pod found."
        return
    fi
    echo "Port-forwarding local port 8200 to $vault_pod:8200 in namespace $NAMESPACE..."
    echo "Use Ctrl+C to stop port-forwarding."
    kubectl port-forward -n $NAMESPACE $vault_pod 8200:8200
}

# Function to port-forward Jenkins
port_forward_jenkins() {
    print_header "Port-forward Jenkins"
    local jenkins_pod=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=jenkins -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
    if [ -z "$jenkins_pod" ]; then
        print_error "No Jenkins pod found."
        return
    fi
    echo "Port-forwarding local port 8080 to $jenkins_pod:8080 in namespace $NAMESPACE..."
    echo "Use Ctrl+C to stop port-forwarding."
    kubectl port-forward -n $NAMESPACE $jenkins_pod 8080:8080
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
    echo "16. Full cleanup/reset (Planescape & ESO)"
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
    echo "Database Inspection:"
    echo "15. List PostgreSQL tables and data"
    echo
    echo "Jenkins Job Inspection:"
    echo "19. List Jenkins jobs"
    echo "20. Show last build status for all jobs"
    echo "21. Show console output for a specific job"
    echo
    echo "17. Full install (ESO + Planescape)"
    echo "18. Toggle log follow mode (currently: $FOLLOW_LOGS)"
    echo
    echo "22. Debug Jenkins SeedJob (Job DSL execution/logs)"
    echo "23. Clean up finalizers of terminating pods (all namespaces)"
    echo
    echo "24. Aggressive finalizer cleanup (pods, pvcs, pvs, statefulsets, namespaces, crds)"
    echo
    echo "25. Validate Groovy Job DSL script (from jenkins-jobdsl-configmap.yaml)"
    echo
    echo "26. Port-forward PostgreSQL pod (local:5432)"
    echo "27. Port-forward Vault pod (local:8200)"
    echo "28. Port-forward Jenkins pod (local:8080)"
    echo
    echo "0. Return to main menu"
    echo "q. Exit"
    echo
    read -p "Select option: " choice
    
    case $choice in
        1) show_pod_status ; show_menu ;;
        2) show_all_resources ; show_menu ;;
        3) describe_problematic_pods ; get_job_logs ; show_menu ;;
        4) get_job_logs ; show_menu ;;
        5) get_events ; show_menu ;;
        6) describe_problematic_pods ; show_menu ;;
        7) get_vault_logs ; show_menu ;;
        8) get_postgresql_logs ; show_menu ;;
        9) get_jenkins_logs ; show_menu ;;
        10) get_eso_logs ; show_menu ;;
        11) follow_logs "vault" ; show_menu ;;
        12) follow_logs "postgresql" ; show_menu ;;
        13) follow_logs "jenkins" ; show_menu ;;
        14) follow_logs "eso" ; show_menu ;;
        15) get_postgres_tables ; show_menu ;;
        16) full_cleanup ; show_menu ;;
        17)
            if [ "$2" = "--verbose" ] || [ "$2" = "-v" ]; then
                full_install true
            else
                full_install false
            fi
            show_menu ;;
        18) toggle_follow_logs ; show_menu ;;
        19) list_jenkins_jobs ; show_menu ;;
        20) show_jenkins_job_status ; show_menu ;;
        21) show_jenkins_console_log ; show_menu ;;
        22) debug_jenkins_seedjob ; show_menu ;;
        23) cleanup_pod_finalizers ; show_menu ;;
        24) cleanup_all_finalizers ; show_menu ;;
        25) validate_groovy_jobdsl ; show_menu ;;
        26) port_forward_postgresql ; show_menu ;;
        27) port_forward_vault ; show_menu ;;
        28) port_forward_jenkins ; show_menu ;;
        0) show_menu ;;
        q) exit 0 ;;
        exit) exit 0 ;;
        *) 
            print_error "Invalid option"
            show_menu
            ;;
    esac
}

# At script start, prompt for log follow mode
if [ $# -eq 0 ]; then
    prompt_follow_logs
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
        "postgres-tables") get_postgres_tables ;;
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
        "full-cleanup")
            full_cleanup
            ;;
        "full-install")
            if [ "$2" = "--verbose" ] || [ "$2" = "-v" ]; then
                full_install true
            else
                full_install false
            fi
            ;;
        "list-jenkins-jobs") list_jenkins_jobs ;;
        "jenkins-job-status") show_jenkins_job_status ;;
        "jenkins-console-log") show_jenkins_console_log ;;
        "debug-jenkins-seedjob") debug_jenkins_seedjob ;;
        "cleanup-pod-finalizers")
            cleanup_pod_finalizers
            ;;
        "cleanup-all-finalizers")
            cleanup_all_finalizers
            ;;
        "validate-groovy-jobdsl") validate_groovy_jobdsl ;;
        "port-forward-postgresql") port_forward_postgresql ;;
        "port-forward-vault") port_forward_vault ;;
        "port-forward-jenkins") port_forward_jenkins ;;
        *)
            echo "Usage: $0 [command]"
            echo
            echo "Commands:"
            echo "  status         - Show pod status"
            echo "  resources      - Show all resources"
            echo "  events         - Show recent events"
            echo "  jobs           - Show Helm job logs"
            echo "  vault          - Show Vault logs"
            echo "  postgresql     - Show PostgreSQL logs"
            echo "  jenkins        - Show Jenkins logs"
            echo "  eso            - Show ESO logs"
            echo "  postgres-tables - List PostgreSQL tables and data"
            echo "  describe       - Describe problematic pods"
            echo "  follow <component> - Follow logs in real-time"
            echo "  quick          - Quick troubleshooting overview"
            echo "  full-cleanup   - Full cluster cleanup/reset (Planescape & ESO)"
            echo "  full-install   - Full cluster install (ESO + Planescape)"
            echo "  list-jenkins-jobs - List Jenkins jobs"
            echo "  jenkins-job-status - Show last build status for all jobs"
            echo "  jenkins-console-log - Show console output for a specific job"
            echo "  debug-jenkins-seedjob - Debug Jenkins SeedJob (Job DSL execution/logs)"
            echo "  cleanup-pod-finalizers - Clean up finalizers of terminating pods (all namespaces)"
            echo "  cleanup-all-finalizers - Aggressive finalizer cleanup (pods, pvcs, pvs, statefulsets, namespaces, crds)"
            echo "  validate-groovy-jobdsl - Validate Groovy Job DSL script from jenkins-jobdsl-configmap.yaml"
            echo "  port-forward-postgresql - Port-forward PostgreSQL pod to local:5432"
            echo "  port-forward-vault      - Port-forward Vault pod to local:8200"
            echo "  port-forward-jenkins    - Port-forward Jenkins pod to local:8080"
            echo
            echo "Run without arguments for interactive menu."
            ;;
    esac
fi 