# Planescape Operator Deployment Guide

This guide provides detailed instructions for deploying the Planescape Operator in different environments.

## Table of Contents
- [Prerequisites](#prerequisites)
- [Environment Selection](#environment-selection)
- [Installation Steps](#installation-steps)
- [Configuration](#configuration)
- [Post-Installation](#post-installation)
- [Environment-Specific Notes](#environment-specific-notes)
- [Troubleshooting](#troubleshooting)

## Prerequisites

### Required Components
- Kubernetes cluster (v1.24+)
- Helm 3.x
- kubectl configured to access your cluster
- Access to required container registries

### Optional Components
- Prometheus and Grafana for metrics monitoring
- Vault for secret management
- Cert-Manager for TLS certificate management

## Environment Selection

The operator supports multiple deployment environments, each with specific configurations:

1. **Local Development** (`k8s/envs/local/values.yaml`)
   - Minimal resource requirements
   - Security features disabled
   - Debug logging enabled
   - Port forwarding configured
   - Suitable for development and testing

2. **Development** (`k8s/envs/dev/values.yaml`)
   - Similar to local but with some security features
   - Suitable for development environments

3. **Staging** (`k8s/envs/staging/values.yaml`)
   - Production-like configuration
   - Security features enabled
   - Monitoring enabled
   - Suitable for pre-production testing

4. **Production** (`k8s/envs/prod/values.yaml`)
   - Full security features
   - High availability
   - Production-grade monitoring
   - Resource limits optimized for production workloads

## Installation Steps

### 1. Add Required Helm Repositories

```bash
# Add required chart repositories
helm repo add bitnami https://charts.bitnami.com/bitnami
helm repo add jenkins https://charts.jenkins.io
helm repo add hashicorp https://helm.releases.hashicorp.com
helm repo update
```

### 2. Install the CRDs

```bash
# Install both CRDs from the k8s/crds directory
kubectl apply -f k8s/crds/planescapejob.yaml
kubectl apply -f k8s/crds/planescapestack.yaml

# Verify CRD installation
kubectl get crds | grep planescape
```

> **Note**: The CRDs are located in the `k8s/crds` directory and use the Kubernetes v1 API format with OpenAPI v3 schema validation. This ensures compatibility with Kubernetes 1.24+ and provides better validation and documentation of the custom resources. The CRDs include comprehensive validation rules, status subresources, and additional printer columns for better visibility.

### 3. Create Required Namespaces

```bash
# Create operator namespace
kubectl create namespace planescape-system

# Create stack namespace (if not using default)
kubectl create namespace planescape
```

### 4. Install the Operator

```bash
# For local development

   # 1. Run the setup script which will:
   ./scripts/setup-local.sh
   # - Generate secure passwords
   # - Create Kubernetes secrets
   # - Set up the environment
   
   helm install planescape-operator k8s/operator/deploy/helm \
   --namespace planescape-system \
   -f k8s/envs/local/values.yaml

# For other environments, use the appropriate values file
helm install planescape-operator k8s/operator/deploy/helm \
  --namespace planescape-system \
  -f k8s/envs/<environment>/values.yaml
```

## Configuration

### Basic Configuration

The operator can be configured through the values file. Key configuration sections include:

1. **Image Configuration**
   ```yaml
   image:
     repository: your-registry/planescape-operator
     tag: latest
     pullPolicy: IfNotPresent
   ```

2. **Resource Configuration**
   ```yaml
   resources:
     limits:
       cpu: 200m
       memory: 256Mi
     requests:
       cpu: 100m
       memory: 128Mi
   ```

3. **Metrics Configuration**
   ```yaml
   metrics:
     enabled: true
     serviceMonitor:
       enabled: true
       namespace: monitoring
   ```

### Stack Configuration

The operator manages three main components:

1. **PostgreSQL**
   - Database configuration
   - Authentication settings
   - Persistence configuration
   - Resource limits

2. **Jenkins**
   - Controller configuration
   - Plugin management
   - Security settings
   - Resource allocation

3. **Vault**
   - Server configuration
   - HA settings
   - Policy management
   - Role configuration

## Post-Installation

### 1. Verify Installation

```bash
# Check operator pod status
kubectl get pods -n planescape-system

# Check operator logs
kubectl logs -n planescape-system -l app.kubernetes.io/name=planescape-operator

# Verify metrics endpoint
kubectl port-forward -n planescape-system svc/planescape-operator 8000:8000
curl localhost:8000/metrics
```

### 2. Configure Vault Integration

1. Create TLS certificates (if not using dev mode):
```bash
kubectl create secret generic vault-tls-ca \
  --namespace planescape-system \
  --from-file=ca.crt=/path/to/ca.crt \
  --from-file=tls.crt=/path/to/tls.crt \
  --from-file=tls.key=/path/to/tls.key
```

2. Create service accounts:
```bash
kubectl create serviceaccount job-runner -n planescape
kubectl create serviceaccount jenkins -n planescape
kubectl create serviceaccount postgresql -n planescape
```

### 3. Deploy Your First Resources

1. Create a basic stack:
```bash
kubectl apply -f docs/examples/stacks/basic-stack.yaml
```

2. Create a sample job:
```bash
kubectl apply -f docs/examples/jobs/one-time-job.yaml
```

## Environment-Specific Notes

### Local Development
- Uses minimal resource requirements
- Security features are disabled
- Debug logging is enabled
- Port forwarding is configured for easy access
- Vault runs in dev mode
- Jenkins security is disabled

### Production
- Full security features enabled
- High availability configured
- Resource limits optimized for production
- Monitoring and alerting enabled
- TLS required for all components
- Network policies enforced

## Troubleshooting

### Common Issues

1. **Operator Pod CrashLoopBackOff**
   - Check logs: `kubectl logs -n planescape-system -l app.kubernetes.io/name=planescape-operator`
   - Verify CRD installation
   - Check resource limits
   - Verify Helm chart repository access

2. **Vault Integration Issues**
   - Verify TLS certificates
   - Check Vault connectivity
   - Review Vault policies and roles
   - Check service account configurations

3. **Stack Deployment Issues**
   - Check component logs
   - Verify Helm chart versions
   - Review values configuration
   - Check resource availability

4. **Job Execution Issues**
   - Check job pod logs
   - Verify service account permissions
   - Check Vault token access
   - Review job template configuration

### Getting Help

- Check the [troubleshooting guide](troubleshooting.md)
- Open an issue on GitHub
- Join our community Slack channel

## Upgrading

1. Update Helm repositories:
```bash
helm repo update
```

2. Upgrade the operator:
```bash
helm upgrade planescape-operator k8s/operator/deploy/helm \
  --namespace planescape-system \
  -f k8s/envs/<environment>/values.yaml
```

3. Verify the upgrade:
```bash
kubectl get pods -n planescape-system
kubectl logs -n planescape-system -l app.kubernetes.io/name=planescape-operator
```

## Uninstallation

1. Delete custom resources:
```bash
# Delete all jobs and stacks
kubectl delete planescapejobs --all --all-namespaces
kubectl delete planescapestacks --all --all-namespaces

# Wait for resources to be cleaned up
kubectl wait --for=delete planescapejobs --all --all-namespaces --timeout=300s
kubectl wait --for=delete planescapestacks --all --all-namespaces --timeout=300s
```

2. Uninstall the operator:
```bash
helm uninstall planescape-operator -n planescape-system
```

3. Delete CRDs:
```bash
kubectl delete crd planescapejobs.planescape.io planescapestacks.planescape.io
```

4. Clean up namespaces:
```bash
kubectl delete namespace planescape-system
kubectl delete namespace planescape  # Only if you want to remove the stack namespace
``` 