# PlanescapeStackOperator

A Kubernetes operator for managing Planescape stacks, which orchestrates the deployment and configuration of Vault, PostgreSQL, and Jenkins using Helm charts with integrated secrets management.

## Overview

The PlanescapeStackOperator automates the deployment and management of complete development stacks consisting of:

- **HashiCorp Vault** - Secrets management and dynamic credential generation
- **PostgreSQL** - Database with Vault-managed dynamic credentials
- **Jenkins** - CI/CD platform with Vault-stored credentials

## Features

- 🔐 **Automated Secrets Management** - Vault handles all password generation and rotation
- 🗄️ **Dynamic Database Credentials** - PostgreSQL credentials generated on-demand by Vault
- 🔄 **Integrated CI/CD** - Jenkins with Vault-managed authentication
- 📦 **Helm-based Deployments** - Uses Bitnami charts for reliable, production-ready deployments
- 🎯 **Declarative Configuration** - Define your entire stack in a single Kubernetes resource
- 🔧 **Auto-healing** - Operator ensures desired state is maintained

## Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│     Vault       │    │   PostgreSQL    │    │    Jenkins      │
│                 │    │                 │    │                 │
│ • Secrets Mgmt  │◄──►│ • Dynamic Creds │    │ • Vault Auth    │
│ • Auto-unseal   │    │ • Bitnami Chart │    │ • Bitnami Chart │
│ • Database Eng. │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         ▲                       ▲                       ▲
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
                    ┌─────────────────┐
                    │ PlanescapeStack │
                    │   Operator      │
                    │                 │
                    │ • Orchestration │
                    │ • Health Checks │
                    │ • Lifecycle Mgmt│
                    └─────────────────┘
```

## Quick Start

### Prerequisites

- Kubernetes cluster (1.19+)
- kubectl configured
- Helm 3.x installed

### Installation

1. **Deploy the operator:**
   ```bash
   kubectl apply -f https://raw.githubusercontent.com/your-org/planescape-operator/main/deploy/operator.yaml
   ```

2. **Create a PlanescapeStack:**
   ```yaml
   apiVersion: planescape.io/v1alpha1
   kind: PlanescapeStack
   metadata:
     name: demo-stack
     namespace: planescape-system
   spec:
     components:
       vault:
         enabled: true
         server:
           resources:
             requests:
               cpu: "100m"
               memory: "256Mi"
             limits:
               cpu: "500m"
               memory: "512Mi"
       postgresql:
         enabled: true
         auth:
           database: "postgres"
         primary:
           persistence:
             size: "8Gi"
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
           serviceType: "ClusterIP"
           persistence:
             size: "8Gi"
           resources:
             requests:
               cpu: "100m"
               memory: "256Mi"
             limits:
               cpu: "500m"
               memory: "512Mi"
   ```

3. **Apply the stack:**
   ```bash
   kubectl apply -f planescapestack.yaml
   ```

### Local Development

For local development with port-forwarding:

```bash
# Set environment variable for local Vault access
export VAULT_LOCAL_PORT_FORWARD=true

# Port-forward Vault
kubectl port-forward -n planescape-system svc/demo-stack-vault-operator 8200:8200 &

# Run the operator locally
cd PlanescapeStackOperator
dotnet run
```

## Configuration

### PlanescapeStack Specification

The `PlanescapeStack` custom resource supports the following configuration:

#### Vault Configuration
```yaml
spec:
  components:
    vault:
      enabled: true
      server:
        ha:
          enabled: false
          replicas: 3
        resources:
          requests:
            cpu: "100m"
            memory: "256Mi"
          limits:
            cpu: "500m"
            memory: "512Mi"
        service:
          type: "ClusterIP"
          port: 8200
        dataStorage:
          size: "10Gi"
```

#### PostgreSQL Configuration
```yaml
spec:
  components:
    postgresql:
      enabled: true
      auth:
        database: "postgres"
        username: "postgres"
      primary:
        persistence:
          size: "8Gi"
        resources:
          requests:
            cpu: "100m"
            memory: "256Mi"
          limits:
            cpu: "500m"
            memory: "512Mi"
        service:
          type: "ClusterIP"
          port: 5432
```

#### Jenkins Configuration
```yaml
spec:
  components:
    jenkins:
      enabled: true
      controller:
        serviceType: "ClusterIP"
        persistence:
          size: "8Gi"
        resources:
          requests:
            cpu: "100m"
            memory: "256Mi"
          limits:
            cpu: "500m"
            memory: "512Mi"
```

## Secrets Management

The operator implements a comprehensive secrets management strategy:

### Vault Integration
- **Auto-initialization** - Vault is automatically initialized with 5 key shares and threshold of 3
- **Auto-unsealing** - Vault is automatically unsealed using stored keys
- **Database secrets engine** - Configured for PostgreSQL dynamic credentials
- **KV secrets engine** - Used for Jenkins and other application secrets

### PostgreSQL Integration
- **Dynamic credentials** - Database users created on-demand with configurable TTL
- **Secure passwords** - All passwords are cryptographically generated
- **Vault-managed** - No hardcoded credentials in configurations

### Jenkins Integration
- **Vault-stored credentials** - Admin passwords stored in Vault KV store
- **Auto-generated** - Secure passwords generated during deployment

## Monitoring and Health Checks

The operator provides comprehensive health monitoring:

- **Component Status** - Individual health status for each component
- **Stack Conditions** - Overall stack health and readiness
- **Automatic Recovery** - Self-healing capabilities for common issues

Check stack status:
```bash
kubectl get planescapestack demo-stack -o yaml
```

## Troubleshooting

### Common Issues

1. **Vault not initializing**
   ```bash
   # Check Vault pod logs
   kubectl logs -n planescape-system demo-stack-vault-operator-0
   
   # Check operator logs
   kubectl logs -n planescape-system deployment/planescape-operator
   ```

2. **PostgreSQL connection issues**
   ```bash
   # Verify PostgreSQL is running
   kubectl get pods -n planescape-system | grep postgresql
   
   # Check PostgreSQL secret
   kubectl get secret -n planescape-system demo-stack-postgresql
   ```

3. **Jenkins not starting**
   ```bash
   # Check Jenkins pod status
   kubectl get pods -n planescape-system | grep jenkins
   
   # View Jenkins logs
   kubectl logs -n planescape-system demo-stack-jenkins-0
   ```

### Debug Mode

Enable debug logging by setting the log level:
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: operator-config
data:
  appsettings.json: |
    {
      "Logging": {
        "LogLevel": {
          "Default": "Debug"
        }
      }
    }
```

## Development

### Building

```bash
# Build the operator
cd PlanescapeStackOperator
dotnet build

# Run tests
dotnet test

# Build Docker image
docker build -t planescape-operator:latest .
```

### Project Structure

```
PlanescapeStackOperator/
├── Controller/
│   ├── PlanescapeStackController.cs    # Main stack controller
│   └── PlanescapeJobController.cs      # Job controller
├── Entities/
│   ├── PlanescapeStack.cs              # Stack CRD definition
│   └── PlanescapeJob.cs                # Job CRD definition
├── Services/
│   ├── VaultService.cs                 # Vault integration
│   ├── HelmService.cs                  # Helm chart management
│   └── StackHealthService.cs           # Health monitoring
├── Program.cs                          # Application entry point
└── appsettings.json                    # Configuration
```

### Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

## Security Considerations

- **Secrets Rotation** - Vault handles automatic credential rotation
- **Network Security** - All communication uses Kubernetes service mesh
- **RBAC** - Operator runs with minimal required permissions
- **Encryption** - All secrets encrypted at rest and in transit

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

For support and questions:
- Create an issue in this repository
- Check the troubleshooting section above
- Review the operator logs for detailed error information 