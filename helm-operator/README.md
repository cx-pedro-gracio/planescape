# Planescape Helm Operator

A Kubernetes operator built using the Operator SDK Helm plugin that manages Planescape stacks and jobs through Helm charts. This operator provides the same functionality as the original .NET operator but with improved maintainability and GitOps integration.

## Overview

The Planescape Helm Operator uses the `helm.sdk.operatorframework.io/v1` API to manage:
- **PlanescapeStack**: Complete infrastructure stacks including PostgreSQL, Jenkins, and Vault
- **PlanescapeJob**: Batch jobs and cron jobs with Vault integration

## Features

- ✅ **Declarative Configuration**: Define infrastructure as code using Kubernetes CRDs
- ✅ **GitOps Ready**: Full integration with GitOps workflows
- ✅ **Helm-based**: Leverages proven Helm charts for deployment
- ✅ **Vault Integration**: Automatic secret management with HashiCorp Vault
- ✅ **Health Monitoring**: Built-in health checks for all components
- ✅ **Multi-Environment**: Support for dev, staging, and production environments

## Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│  PlanescapeStack │    │  Helm Operator   │    │  Helm Charts    │
│      CRD        │───▶│                  │───▶│                 │
└─────────────────┘    │  - Watches CRDs  │    │  - PostgreSQL   │
                       │  - Reconciles     │    │  - Jenkins      │
┌─────────────────┐    │  - Health Checks  │    │  - Vault        │
│  PlanescapeJob  │───▶│                  │    │  - Jobs         │
│      CRD        │    └──────────────────┘    └─────────────────┘
└─────────────────┘
```

## Quick Start

### Prerequisites

- Kubernetes cluster (v1.24+)
- kubectl configured
- Helm 3.12+
- Docker (for building custom images)

### Installation

1. **Install CRDs**:
   ```bash
   kubectl apply -f config/crd/bases/
   ```

2. **Deploy the Operator**:
   ```bash
   kubectl apply -f config/rbac/role.yaml
   kubectl apply -f config/manager/manager.yaml
   ```

3. **Verify Installation**:
   ```bash
   kubectl get pods -n planescape-system
   kubectl get crd | grep planescape
   ```

### Creating Your First Stack

1. **Create a PlanescapeStack**:
   ```yaml
   apiVersion: planescape.io/v1alpha1
   kind: PlanescapeStack
   metadata:
     name: my-stack
     namespace: planescape-system
   spec:
     components:
       postgresql:
         enabled: true
         auth:
           database: "myapp"
           username: "postgres"
       jenkins:
         enabled: true
       vault:
         enabled: true
   ```

2. **Apply the Configuration**:
   ```bash
   kubectl apply -f my-stack.yaml
   ```

3. **Monitor the Deployment**:
   ```bash
   kubectl get planescapestacks
   kubectl describe planescapestack my-stack
   ```

## Configuration

### PlanescapeStack Specification

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeStack
metadata:
  name: example-stack
spec:
  components:
    postgresql:
      enabled: true
      auth:
        database: "myapp"
        username: "postgres"
      primary:
        persistence:
          size: "10Gi"
        resources:
          requests:
            cpu: "250m"
            memory: "512Mi"
          limits:
            cpu: "500m"
            memory: "1Gi"
    
    jenkins:
      enabled: true
      controller:
        serviceType: ClusterIP
        persistence:
          size: "8Gi"
        resources:
          requests:
            cpu: "200m"
            memory: "512Mi"
        installPlugins:
          - "kubernetes:1.31.3"
          - "workflow-aggregator:2.6"
    
    vault:
      enabled: true
      server:
        ha:
          enabled: false
        resources:
          requests:
            cpu: "200m"
            memory: "256Mi"
        dataStorage:
          size: "10Gi"
```

### PlanescapeJob Specification

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeJob
metadata:
  name: example-job
spec:
  schedule: "0 2 * * *"  # Daily at 2 AM
  vault:
    enabled: true
    role: "my-job-role"
    path: "database/creds/my-stack"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: worker
            image: my-app:latest
            command: ["./run-job.sh"]
          restartPolicy: Never
```

## Development

### Building the Operator

```bash
# Build Docker image
make docker-build

# Build and push multi-platform image
make docker-buildx IMG=ghcr.io/myorg/planescape-helm-operator:latest
```

### Local Development

```bash
# Validate charts and CRDs
make validate

# Deploy to local cluster
make deploy

# Deploy examples
make deploy-examples

# View logs
make logs

# Clean up
make clean
```

### Testing

```bash
# Test stack creation
make test-stack

# Test job creation
make test-job

# Run all tests
make test-all
```

## GitOps Integration

The operator is designed for GitOps workflows:

1. **Repository Structure**:
   ```
   environments/
   ├── dev/
   │   ├── stack.yaml
   │   └── jobs/
   ├── staging/
   │   ├── stack.yaml
   │   └── jobs/
   └── prod/
       ├── stack.yaml
       └── jobs/
   ```

2. **Automated Deployment**: Changes to the repository trigger CI/CD pipelines that apply configurations to the appropriate environments.

3. **Environment Promotion**: Promote configurations from dev → staging → prod through Git workflows.

## Monitoring and Observability

### Health Checks

The operator monitors the health of deployed components:

- **PostgreSQL**: StatefulSet readiness
- **Jenkins**: Deployment readiness and HTTP health checks
- **Vault**: StatefulSet readiness and seal status

### Metrics

The operator exposes Prometheus metrics on port 8080:

- `helm_operator_reconcile_duration_seconds`
- `helm_operator_reconcile_total`
- `helm_operator_reconcile_errors_total`

### Logging

Structured logging with configurable levels:

```bash
# View operator logs
kubectl logs -f deployment/planescape-helm-operator -n planescape-system

# Increase log verbosity
kubectl patch deployment planescape-helm-operator -n planescape-system -p '{"spec":{"template":{"spec":{"containers":[{"name":"operator","args":["--zap-log-level=debug"]}]}}}}'
```

## Troubleshooting

### Common Issues

1. **CRD Not Found**:
   ```bash
   kubectl apply -f config/crd/bases/
   ```

2. **RBAC Permissions**:
   ```bash
   kubectl apply -f config/rbac/role.yaml
   ```

3. **Helm Chart Issues**:
   ```bash
   # Validate charts
   helm lint charts/
   helm template test charts/
   ```

4. **Resource Status**:
   ```bash
   kubectl describe planescapestack <name>
   kubectl get events --sort-by='.lastTimestamp'
   ```

### Debug Commands

```bash
# Check operator status
make status

# View detailed resource information
kubectl describe planescapestack <name> -n <namespace>

# Check Helm releases
helm list -A

# View operator logs with debug level
kubectl logs deployment/planescape-helm-operator -n planescape-system --tail=100
```

## Migration from .NET Operator

To migrate from the existing .NET operator:

1. **Export Existing Configurations**:
   ```bash
   kubectl get planescapestacks -o yaml > existing-stacks.yaml
   kubectl get planescapejobs -o yaml > existing-jobs.yaml
   ```

2. **Update Resource Definitions**: Convert to the new CRD format if needed.

3. **Deploy Helm Operator**: Follow the installation steps above.

4. **Apply Configurations**: Apply the updated resource definitions.

5. **Verify Migration**: Ensure all components are running correctly.

6. **Remove Old Operator**: Clean up the .NET operator deployment.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](../LICENSE) file for details. 