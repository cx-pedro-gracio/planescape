# Troubleshooting Guide

This guide covers common issues and their solutions for both operator-based and Helm-based deployments of Planescape components.

## Table of Contents
- [Common Issues](#common-issues)
- [Operator-Specific Issues](#operator-specific-issues)
- [Helm-Based Deployment Issues](#helm-based-deployment-issues)
- [Component-Specific Issues](#component-specific-issues)
- [Logging and Debugging](#logging-and-debugging)

## Common Issues

### Cluster Access Issues

#### Cannot Connect to Kubernetes Cluster
```bash
# Check cluster connection
kubectl cluster-info

# Verify kubeconfig
kubectl config view

# Check current context
kubectl config current-context
```

#### Namespace Issues
```bash
# List all namespaces
kubectl get namespaces

# Check if required namespaces exist
kubectl get namespace planescape-system
kubectl get namespace planescape

# Create missing namespaces
kubectl create namespace planescape-system
kubectl create namespace planescape
```

### Resource Issues

#### Insufficient Resources
```bash
# Check node resources
kubectl describe nodes

# Check pod resource usage
kubectl top pods -n planescape-system
kubectl top pods -n planescape

# Check resource quotas
kubectl get resourcequota -n planescape-system
kubectl get resourcequota -n planescape
```

## Operator-Specific Issues

### Operator Deployment Issues

#### Operator Pod Not Starting
```bash
# Check operator pod status
kubectl get pods -n planescape-system -l app.kubernetes.io/name=planescape-operator

# Check operator logs
kubectl logs -n planescape-system -l app.kubernetes.io/name=planescape-operator

# Check operator events
kubectl get events -n planescape-system --sort-by='.lastTimestamp'
```

#### CRD Installation Issues
```bash
# Verify CRD installation
kubectl get crds | grep planescape

# Check CRD status
kubectl get crd planescapestacks.planescape.io -o yaml
kubectl get crd planescapejobs.planescape.io -o yaml

# Reinstall CRDs if needed
kubectl apply -f k8s/crds/planescapejob.yaml
kubectl apply -f k8s/crds/planescapestack.yaml
```

### Stack Management Issues

#### Stack Not Reconciling
```bash
# Check stack status
kubectl get planescapestack -n planescape-system
kubectl describe planescapestack -n planescape-system

# Check operator logs for reconciliation
kubectl logs -n planescape-system -l app.kubernetes.io/name=planescape-operator | grep "reconciling"

# Verify stack events
kubectl get events -n planescape-system --field-selector involvedObject.kind=PlanescapeStack
```

#### Stack Component Issues
```bash
# Check component status in stack
kubectl get planescapestack -n planescape-system -o jsonpath='{.status.components}'

# Check component pods
kubectl get pods -n planescape -l app.kubernetes.io/part-of=planescape-stack

# Check component logs
kubectl logs -n planescape -l app.kubernetes.io/part-of=planescape-stack
```

## Helm-Based Deployment Issues

### Helm Installation Issues

#### Chart Installation Fails
```bash
# Check Helm repositories
helm repo list

# Update Helm repositories
helm repo update

# Verify chart dependencies
helm dependency list planescape/charts

# Check chart values
helm show values planescape/charts

# Debug installation
helm install planescape planescape/charts --debug --dry-run
```

#### Upgrade Issues
```bash
# Check release history
helm history planescape

# Rollback to previous version
helm rollback planescape <revision>

# Check release status
helm status planescape
```

### Component Deployment Issues

#### Component Pod Issues
```bash
# List all releases
helm list -A

# Check component pods
kubectl get pods -n planescape-system -l app.kubernetes.io/instance=planescape

# Check component events
kubectl get events -n planescape-system --field-selector involvedObject.namespace=planescape-system
```

#### Component Configuration Issues
```bash
# Verify values
helm get values planescape

# Check component configuration
kubectl get configmap -n planescape-system -l app.kubernetes.io/instance=planescape
kubectl get secret -n planescape-system -l app.kubernetes.io/instance=planescape
```

## Component-Specific Issues

### Vault Issues

#### Vault Unsealing Issues
```bash
# Check Vault status
kubectl exec -n planescape-system deploy/planescape-vault -- vault status

# Check Vault logs
kubectl logs -n planescape-system -l app.kubernetes.io/name=vault

# Check Vault events
kubectl get events -n planescape-system --field-selector involvedObject.name=planescape-vault
```

#### Vault Authentication Issues
```bash
# Check Vault auth methods
kubectl exec -n planescape-system deploy/planescape-vault -- vault auth list

# Check Vault policies
kubectl exec -n planescape-system deploy/planescape-vault -- vault policy list

# Check Vault roles
kubectl exec -n planescape-system deploy/planescape-vault -- vault list auth/kubernetes/role
```

### PostgreSQL Issues

#### Database Connection Issues
```bash
# Check PostgreSQL pod status
kubectl get pods -n planescape-system -l app.kubernetes.io/name=postgresql

# Check PostgreSQL logs
kubectl logs -n planescape-system -l app.kubernetes.io/name=postgresql

# Test database connection
kubectl exec -n planescape-system deploy/planescape-postgresql -- psql -U postgres -d postgres -c "\l"
```

#### Database Performance Issues
```bash
# Check PostgreSQL metrics
kubectl port-forward -n planescape-system svc/planescape-postgresql 5432:5432
psql -h localhost -U postgres -d postgres -c "SELECT * FROM pg_stat_activity;"

# Check resource usage
kubectl top pod -n planescape-system -l app.kubernetes.io/name=postgresql
```

### Jenkins Issues

#### Jenkins Startup Issues
```bash
# Check Jenkins pod status
kubectl get pods -n planescape-system -l app.kubernetes.io/name=jenkins

# Check Jenkins logs
kubectl logs -n planescape-system -l app.kubernetes.io/name=jenkins

# Check Jenkins events
kubectl get events -n planescape-system --field-selector involvedObject.name=planescape-jenkins
```

#### Jenkins Plugin Issues
```bash
# Check installed plugins
kubectl exec -n planescape-system deploy/planescape-jenkins -- jenkins-plugin-cli --list

# Check plugin updates
kubectl exec -n planescape-system deploy/planescape-jenkins -- jenkins-plugin-cli --available-updates

# Update plugins
kubectl exec -n planescape-system deploy/planescape-jenkins -- jenkins-plugin-cli --update
```

## Logging and Debugging

### Operator Logging

#### Enable Debug Logging
```bash
# For operator-based deployment
kubectl patch deployment planescape-operator -n planescape-system --patch '{"spec":{"template":{"spec":{"containers":[{"name":"operator","env":[{"name":"LOG_LEVEL","value":"debug"}]}]}}}}'

# For Helm-based deployment
helm upgrade planescape planescape/charts --set operator.logLevel=debug
```

#### Collect Operator Logs
```bash
# Get operator logs
kubectl logs -n planescape-system -l app.kubernetes.io/name=planescape-operator > operator.log

# Get operator events
kubectl get events -n planescape-system --sort-by='.lastTimestamp' > operator-events.log
```

### Component Logging

#### Enable Component Debug Logging
```bash
# For operator-based deployment
kubectl patch planescapestack -n planescape-system <stack-name> --patch '{"spec":{"components":{"vault":{"server":{"logLevel":"debug"}}}}}'

# For Helm-based deployment
helm upgrade planescape planescape/charts --set vault.server.logLevel=debug
```

#### Collect Component Logs
```bash
# Get all component logs
kubectl logs -n planescape-system -l app.kubernetes.io/part-of=planescape > components.log

# Get component events
kubectl get events -n planescape-system --field-selector involvedObject.namespace=planescape-system > component-events.log
```

### Debugging Tools

#### Network Debugging
```bash
# Check network policies
kubectl get networkpolicy -n planescape-system

# Test component connectivity
kubectl run -n planescape-system tmp-shell --rm -i --tty --image nicolaka/netshoot -- /bin/bash
```

#### Resource Debugging
```bash
# Check resource usage
kubectl top nodes
kubectl top pods -n planescape-system

# Check resource limits
kubectl describe nodes | grep -A 5 "Allocated resources"
```

#### Storage Debugging
```bash
# Check persistent volumes
kubectl get pv
kubectl get pvc -n planescape-system

# Check storage classes
kubectl get storageclass
```

## Getting Help

If you're still experiencing issues:

1. Check the [GitHub Issues](https://github.com/your-org/planescape/issues) for similar problems
2. Review the [Security Guide](security.md) for security-related issues
3. Join our [Slack Channel](https://slack.planescape.io) for community support
4. Contact [Support](mailto:support@planescape.io) for enterprise support 