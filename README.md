# Planescape

Planescape is a Kubernetes operator that simplifies the management of jobs and infrastructure stacks in Kubernetes clusters. It provides a unified interface for deploying and managing Jenkins, PostgreSQL, and Vault, along with job scheduling capabilities.

## Features

### Job Management
- **One-time Jobs**: Run single execution tasks
- **CronJobs**: Schedule recurring tasks with cron expressions
- **Job Templates**: Reusable job definitions
- **Secret Management**: Secure credential handling with Vault integration
- **Resource Control**: Configurable resource limits and requests

### Stack Management
- **Infrastructure as Code**: Define your entire stack in Kubernetes resources
- **Component Integration**:
  - **PostgreSQL**: Managed database with automatic backups
  - **Jenkins**: CI/CD automation with plugin management
  - **Vault**: Secret management with policy enforcement
- **Resource Profiles**: Predefined resource configurations (small, medium, large)
- **High Availability**: Configurable HA for production deployments

### Monitoring & Observability
- **Metrics**: Prometheus metrics for operator and components
- **Logging**: Structured logging with correlation IDs
- **Health Checks**: Built-in health monitoring
- **Grafana Dashboards**: Pre-configured monitoring dashboards

## Quick Start

### Prerequisites
- Kubernetes cluster (v1.24+)
- Helm 3.x
- kubectl configured to access your cluster
- (Optional) Prometheus and Grafana for monitoring
- (Optional) Vault for secret management

### Installation

1. Install CRDs:
```bash
kubectl apply -f k8s/crds/planescapejob.yaml
kubectl apply -f k8s/crds/planescapestack.yaml
```

2. Deploy the operator:
```bash
# For local development
helm install planescape-operator k8s/operator/deploy/helm \
  --namespace planescape-system \
  -f k8s/envs/local/values.yaml
```

3. Create your first resources:
```bash
# Deploy a basic stack
kubectl apply -f docs/examples/stacks/basic-stack.yaml

# Create a sample job
kubectl apply -f docs/examples/jobs/one-time-job.yaml
```

## Project Structure

```
.
├── k8s/                    # Kubernetes manifests
│   ├── crds/              # Custom Resource Definitions
│   ├── envs/              # Environment-specific configurations
│   │   ├── local/        # Local development
│   │   ├── dev/          # Development environment
│   │   ├── staging/      # Staging environment
│   │   └── prod/         # Production environment
│   └── operator/          # Operator-specific resources
│       ├── deploy/       # Deployment manifests
│       └── src/          # Operator source code
├── docs/                  # Documentation
│   ├── guides/           # User guides
│   └── examples/         # Example configurations
└── tests/                # Test suites
```

## Documentation

- [Deployment Guide](docs/guides/deployment.md): Detailed deployment instructions
- [Job Management](docs/guides/jobs.md): Guide to managing jobs
- [Stack Management](docs/guides/stacks.md): Guide to managing infrastructure stacks
- [Security](docs/guides/security.md): Security best practices
- [Troubleshooting](docs/guides/troubleshooting.md): Common issues and solutions

## Examples

### One-time Job
```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeJob
metadata:
  name: example-job
spec:
  schedule: "@once"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: example
            image: busybox
            command: ["echo", "Hello, World!"]
```

### Basic Stack
```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeStack
metadata:
  name: basic-stack
spec:
  components:
    postgresql:
      enabled: true
      auth:
        database: myapp
    jenkins:
      enabled: true
      controller:
        resources:
          requests:
            cpu: 500m
            memory: 1Gi
    vault:
      enabled: true
      server:
        ha:
          enabled: true
```

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

## Security

- Report security issues to security@planescape.io
- See our [Security Policy](SECURITY.md) for more information

## License

Apache License 2.0 - see [LICENSE](LICENSE) for details 