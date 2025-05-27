# Stack Management Guide

This guide covers how to create, configure, and manage infrastructure stacks using the PlanescapeStack custom resource.

## Overview

PlanescapeStack provides a declarative way to deploy and manage complete infrastructure stacks consisting of PostgreSQL, Jenkins, and Vault with integrated secrets management.

## Stack Components

### PostgreSQL

Database component with Vault-managed credentials:

```yaml
spec:
  components:
    postgresql:
      enabled: true
      auth:
        database: "myapp"
        username: "postgres"
      primary:
        persistence:
          size: "20Gi"
        resources:
          requests:
            cpu: "500m"
            memory: "1Gi"
          limits:
            cpu: "1000m"
            memory: "2Gi"
        service:
          type: ClusterIP
          port: 5432
```

### Jenkins

CI/CD platform with plugin management:

```yaml
spec:
  components:
    jenkins:
      enabled: true
      controller:
        serviceType: ClusterIP
        persistence:
          size: "20Gi"
        resources:
          requests:
            cpu: "500m"
            memory: "1Gi"
          limits:
            cpu: "1000m"
            memory: "2Gi"
        installPlugins:
          - "kubernetes:1.31.3"
          - "workflow-aggregator:2.6"
          - "git:4.11.0"
          - "configuration-as-code:1.55"
```

### Vault

Secrets management with auto-initialization:

```yaml
spec:
  components:
    vault:
      enabled: true
      server:
        ha:
          enabled: true
          replicas: 3
        resources:
          requests:
            cpu: "250m"
            memory: "512Mi"
          limits:
            cpu: "500m"
            memory: "1Gi"
        service:
          type: ClusterIP
        dataStorage:
          size: "10Gi"
```

## Stack Configurations

### Development Stack

Minimal resources for local development:

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeStack
metadata:
  name: dev-stack
  namespace: planescape-system
spec:
  components:
    postgresql:
      enabled: true
      auth:
        database: "dev"
      primary:
        persistence:
          size: "5Gi"
        resources:
          requests:
            cpu: "100m"
            memory: "256Mi"
          limits:
            cpu: "500m"
            memory: "512Mi"
    jenkins:
      enabled: true
      controller:
        persistence:
          size: "5Gi"
        resources:
          requests:
            cpu: "200m"
            memory: "512Mi"
          limits:
            cpu: "500m"
            memory: "1Gi"
    vault:
      enabled: true
      server:
        ha:
          enabled: false
        resources:
          requests:
            cpu: "100m"
            memory: "256Mi"
          limits:
            cpu: "250m"
            memory: "512Mi"
        dataStorage:
          size: "5Gi"
```

### Production Stack

High availability with larger resources:

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeStack
metadata:
  name: prod-stack
  namespace: planescape-system
spec:
  components:
    postgresql:
      enabled: true
      auth:
        database: "production"
      primary:
        persistence:
          size: "100Gi"
        resources:
          requests:
            cpu: "2000m"
            memory: "4Gi"
          limits:
            cpu: "4000m"
            memory: "8Gi"
    jenkins:
      enabled: true
      controller:
        persistence:
          size: "50Gi"
        resources:
          requests:
            cpu: "1000m"
            memory: "2Gi"
          limits:
            cpu: "2000m"
            memory: "4Gi"
        installPlugins:
          - "kubernetes:1.31.3"
          - "workflow-aggregator:2.6"
          - "git:4.11.0"
          - "configuration-as-code:1.55"
          - "blueocean:1.25.2"
          - "pipeline-stage-view:2.25"
    vault:
      enabled: true
      server:
        ha:
          enabled: true
          replicas: 3
        resources:
          requests:
            cpu: "500m"
            memory: "1Gi"
          limits:
            cpu: "1000m"
            memory: "2Gi"
        dataStorage:
          size: "50Gi"
```

## Secrets Management

### Automatic Password Generation

The operator automatically generates secure passwords for all components:

- PostgreSQL admin password
- Jenkins admin password
- Vault root token and unseal keys

### Vault Integration

Vault is configured with:

- Database secrets engine for PostgreSQL dynamic credentials
- KV secrets engine for application secrets
- Automatic policy and role creation

### Accessing Secrets

```bash
# Get Vault root token
kubectl get secret -n planescape-system demo-stack-vault-operator-unseal-key \
  -o jsonpath='{.data.root-token}' | base64 -d

# Generate dynamic database credentials
curl -H "X-Vault-Token: $VAULT_TOKEN" \
  http://localhost:8200/v1/database/creds/planescape
```

## Monitoring and Management

### Check Stack Status

```bash
# List all stacks
kubectl get planescapestacks -n planescape-system

# Get detailed status
kubectl describe planescapestack my-stack -n planescape-system

# Check component health
kubectl get pods -n planescape-system -l stack.planescape.io/name=my-stack
```

### Component Access

```bash
# Port-forward to Jenkins
kubectl port-forward -n planescape-system svc/my-stack-jenkins 8080:8080

# Port-forward to Vault
kubectl port-forward -n planescape-system svc/my-stack-vault-operator 8200:8200

# Port-forward to PostgreSQL
kubectl port-forward -n planescape-system svc/my-stack-postgresql 5432:5432
```

### Scaling Components

Update the stack specification to change resources:

```yaml
spec:
  components:
    postgresql:
      primary:
        resources:
          requests:
            cpu: "1000m"    # Increased from 500m
            memory: "2Gi"   # Increased from 1Gi
```

Apply the changes:

```bash
kubectl apply -f my-stack.yaml
```

## Troubleshooting

### Common Issues

1. **Stack not deploying**: Check namespace and RBAC permissions
2. **Components not starting**: Verify resource quotas and node capacity
3. **Vault sealed**: Check unseal keys and initialization status
4. **Database connection failed**: Verify network policies and credentials

### Debug Commands

```bash
# Check operator logs
kubectl logs -n planescape-system deployment/planescape-operator

# Check component events
kubectl get events -n planescape-system --sort-by='.lastTimestamp'

# Describe failing pods
kubectl describe pod -n planescape-system <pod-name>
```

## Best Practices

1. **Use namespaces** to isolate different environments
2. **Set resource limits** to prevent resource exhaustion
3. **Enable monitoring** for production deployments
4. **Regular backups** of persistent data
5. **Test changes** in development before production
6. **Use GitOps** for stack configuration management

## Examples

See the [examples directory](../examples/stacks/) for more stack configurations:

- [Basic stack](../examples/stacks/basic-stack.yaml)
- [Production stack](../examples/stacks/production-stack.yaml)
- [Demo stack](../examples/stacks/demo-stack.yaml) 