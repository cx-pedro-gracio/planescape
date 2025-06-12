# Planescape Umbrella Chart

## Overview

Planescape is a **production-ready DevSecOps platform** that deploys PostgreSQL, Jenkins, and Vault using official Helm charts, with **automated secret management, database schema initialization, and dynamic credentials** managed by Vault. Jenkins jobs can securely log to the database using short-lived credentials provisioned at runtime.

---

## Architecture

- **PostgreSQL**: Deployed via Bitnami's official Helm chart with automated schema initialization
- **Jenkins**: Deployed via the official Jenkins Helm chart with Vault integration and admin credentials from ESO
- **Vault**: Deployed via the official HashiCorp Vault Helm chart with Kubernetes authentication
- **External Secrets Operator**: Manages secret synchronization from Vault to Kubernetes
- **Dynamic Credentials**: Vault automatically issues database credentials for Jenkins jobs
- **Automated Setup**: Helm hooks handle proper initialization order and configuration

---

## Fixed Issues

✅ **Database Schema**: Automated `logs` table creation  
✅ **Jenkins Admin**: Proper admin user configuration from ESO  
✅ **Helm Hooks**: Reliable automated deployment script  
✅ **Security**: Production-ready configurations available  
✅ **RBAC**: Proper permissions for all components  
✅ **Storage**: Minikube storage provisioner auto-fix  

---

## Quick Start

### Two-Chart Architecture (Recommended)
```bash
# Step 1: Install External Secrets Operator (cluster-wide)
helm install external-secrets ./charts/external-secrets/ -n external-secrets-system --create-namespace

# Step 2: Wait for ESO to be ready
kubectl wait --for=condition=available deployment/external-secrets -n external-secrets-system --timeout=300s

# Step 3: Install main Planescape chart
   helm dependency update charts/planescape
helm install planescape ./charts/planescape -n planescape --create-namespace --wait --timeout=15m
```

### Why Two Charts?

**✅ Correct Separation of Concerns:**
- `external-secrets` = **Platform/Infrastructure** (cluster-wide, installed once)
- `planescape` = **Application** (namespace-scoped, can have multiple instances)

**✅ DevOps Best Practices:**
- ESO is typically installed by **platform teams** once per cluster
- Application teams then consume ESO via ClusterSecretStore
- Follows the **infrastructure vs application** boundary

### Development Deployment (If hooks fail)
```bash
# Fallback with deployment script for hook issues
./deploy.sh
```

### Production Deployment
```bash
# Platform team installs ESO once
helm install external-secrets ./charts/external-secrets/ -n external-secrets-system --create-namespace

# Application teams deploy their apps
helm dependency update charts/planescape
helm install planescape ./charts/planescape -n planescape --create-namespace \
  -f charts/planescape/values-production.yaml --wait --timeout=20m
```

---

## Prerequisites
- **Kubernetes cluster** (1.24+)
- **Helm** (3.8+)
- **kubectl** configured
- **Storage provisioner** (auto-fixed for minikube)

---

## How It Works

### 1. **Automated Initialization Sequence**
1. **ESO Deployment** - External Secrets Operator with CRDs
2. **Main Resources** - Vault, PostgreSQL, Jenkins deployed
3. **Hook Weight 0**: Vault Kubernetes authentication setup
4. **Hook Weight 1**: Vault secrets and database engine configuration  
5. **Hook Weight 2**: ESO policies applied to Vault
6. **Hook Weight 3**: PostgreSQL schema initialization (logs table)
7. **Secret Sync** - ESO syncs admin credentials from Vault to K8s
8. **Applications Ready** - All services start with proper credentials

### 2. **Jenkins Job Creation**
- **Admin Access**: Jenkins admin credentials automatically configured from ESO
- **Job DSL**: Automated job creation for logging to PostgreSQL
- **Vault Integration**: Jenkins jobs fetch dynamic DB credentials at runtime
- **Database Access**: Jobs use short-lived credentials to log to PostgreSQL

### 3. **Security Model**
- **No Static Passwords**: All credentials are dynamic or ESO-managed
- **Least Privilege**: RBAC controls access between components
- **Encryption**: TLS for production deployments
- **Audit Logging**: Vault audit logs enabled in production

---

## File Structure

```
charts/planescape/
├── Chart.yaml                              # Umbrella chart definition
├── values.yaml                             # Configuration (dev & prod)
├── templates/
│   ├── _helpers.tpl                        # Helm helper functions
│   ├── vault-auth-setup-job.yaml          # Hook 0: Vault K8s auth
│   ├── vault-init-job.yaml                # Hook 1: Secrets & DB engine
│   ├── vault-eso-policy-job.yaml          # Hook 2: ESO policies
│   ├── postgres-init-job.yaml             # Hook 3: Schema init
│   ├── vault-clustersecretstore-vault.yaml # ESO ClusterSecretStore
│   ├── eso-externalsecret-*.yaml          # ESO secret definitions
│   ├── jenkins-jobdsl-configmap.yaml      # Jenkins Job DSL scripts
│   ├── vault-init-job-rbac.yaml           # RBAC for hooks
│   └── network-policies.yaml              # Network security policies
├── files/
│   └── vault-eso-policy.hcl               # Vault policy for ESO
└── DEBUGGING_FIXES.md                     # Detailed fix documentation
```

---

## Debugging

```bash
# Quick status check
./debug-logs.sh quick

# Component-specific logs
./debug-logs.sh vault
./debug-logs.sh postgresql
./debug-logs.sh jenkins

# Real-time log following
./debug-logs.sh follow vault
```

---

## Customization

### Configuration Options
All settings are in `values.yaml`:

```yaml
# Planescape-specific configuration
planescape:
  vault:
    token: "your-vault-token"  # Change for production!
  database:
    name: "your-database-name"
    user: "your-database-user"
  security:
    enforceSecurityContexts: true
    readOnlyRootFilesystem: true

# Override resource limits
jenkins:
  controller:
    resources:
      limits:
        cpu: 2000m
        memory: 4Gi

# Override database configuration
postgresql:
  auth:
    database: "custom-db-name"
```

### Development vs Production
- **Development**: Uses dev mode Vault with root token
- **Production**: Set `vault.dev.enabled: false` and configure proper authentication

---

## Security Notes

### Development
- ✅ Dynamic database credentials
- ✅ ESO-managed admin secrets  
- ⚠️ Vault dev mode (auto-unsealed)
- ⚠️ Root token authentication

### Production
- ✅ HA Vault with proper unsealing
- ✅ TLS encryption
- ✅ Kubernetes authentication
- ✅ Audit logging
- ✅ Network policies
- ✅ Security contexts

---

## Troubleshooting

### Common Issues
1. **Storage Problems**: Run `./deploy.sh` - auto-fixes minikube storage
2. **Hook Failures**: Use deployment script which runs hooks manually
3. **ESO Auth Issues**: Check ClusterSecretStore status with debug script
4. **Missing Tables**: New postgres-init hook creates schema automatically

### Manual Recovery
```bash
# Clean slate deployment
./deploy.sh cleanup
./deploy.sh deploy

# Check hook execution
kubectl get jobs -n planescape
kubectl logs job/planescape-vault-auth-setup -n planescape
```

---

## Production Readiness Checklist

- [ ] Use `values-production.yaml`
- [ ] Configure proper TLS certificates
- [ ] Set up Vault auto-unsealing (AWS KMS/Azure Key Vault)
- [ ] Configure backup strategies
- [ ] Set up monitoring (Prometheus/Grafana)
- [ ] Apply network policies
- [ ] Configure resource quotas
- [ ] Set up log aggregation
- [ ] Configure alert rules

---

## License
MIT 