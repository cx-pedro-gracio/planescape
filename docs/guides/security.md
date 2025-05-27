# Security Guide

This guide covers security best practices and configurations for both operator-based and Helm-based deployments of Planescape components.

## Table of Contents
- [Security Overview](#security-overview)
- [Vault Integration](#vault-integration)
- [Network Security](#network-security)
- [Authentication & Authorization](#authentication--authorization)
- [Secret Management](#secret-management)
- [TLS Configuration](#tls-configuration)
- [Security Hardening](#security-hardening)

## Security Overview

Planescape provides multiple layers of security:

1. **Vault Integration**
   - Centralized secrets management
   - Dynamic credentials
   - Encryption at rest

2. **Network Security**
   - Network policies
   - Service isolation
   - TLS encryption

3. **Authentication & Authorization**
   - Kubernetes RBAC
   - Vault authentication
   - Service accounts

4. **Secret Management**
   - Vault-based secrets
   - Automatic rotation
   - Access control

## Vault Integration

### Operator-Based Deployment

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeStack
metadata:
  name: secure-stack
  namespace: planescape-system
spec:
  components:
    vault:
      enabled: true
      server:
        ha:
          enabled: true
          replicas: 3
        tls:
          enabled: true
          secretName: vault-tls
        audit:
          enabled: true
          path: /vault/audit
        policies:
          - name: postgres-dynamic
            rules: |
              path "database/creds/postgres" {
                capabilities = ["read"]
              }
          - name: jenkins-auth
            rules: |
              path "auth/jenkins/login" {
                capabilities = ["create", "read"]
              }
```

### Helm-Based Deployment

```yaml
# values.yaml
vault:
  enabled: true
  server:
    ha:
      enabled: true
      replicas: 3
    tls:
      enabled: true
      secretName: vault-tls
    audit:
      enabled: true
      path: /vault/audit
    policies:
      postgres-dynamic: |
        path "database/creds/postgres" {
          capabilities = ["read"]
        }
      jenkins-auth: |
        path "auth/jenkins/login" {
          capabilities = ["create", "read"]
        }
```

## Network Security

### Network Policies

#### Operator-Based Deployment

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: planescape-isolation
  namespace: planescape-system
spec:
  podSelector:
    matchLabels:
      app.kubernetes.io/part-of: planescape-stack
  policyTypes:
    - Ingress
    - Egress
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app.kubernetes.io/part-of: planescape-stack
      ports:
        - protocol: TCP
          port: 8200  # Vault
        - protocol: TCP
          port: 5432  # PostgreSQL
        - protocol: TCP
          port: 8080  # Jenkins
```

#### Helm-Based Deployment

```yaml
# values.yaml
networkPolicy:
  enabled: true
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app.kubernetes.io/instance: planescape
      ports:
        - protocol: TCP
          port: 8200
        - protocol: TCP
          port: 5432
        - protocol: TCP
          port: 8080
```

### Service Isolation

#### Operator-Based Deployment

```yaml
spec:
  components:
    vault:
      server:
        service:
          type: ClusterIP
          port: 8200
    postgresql:
      primary:
        service:
          type: ClusterIP
          port: 5432
    jenkins:
      controller:
        serviceType: ClusterIP
        servicePort: 8080
```

#### Helm-Based Deployment

```yaml
# values.yaml
vault:
  server:
    service:
      type: ClusterIP
      port: 8200
postgresql:
  primary:
    service:
      type: ClusterIP
      port: 5432
jenkins:
  controller:
    serviceType: ClusterIP
    servicePort: 8080
```

## Authentication & Authorization

### Kubernetes RBAC

#### Operator-Based Deployment

```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: planescape-operator
  namespace: planescape-system
rules:
  - apiGroups: [""]
    resources: ["pods", "services", "secrets"]
    verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
  - apiGroups: ["planescape.io"]
    resources: ["planescapestacks", "planescapejobs"]
    verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: planescape-operator
  namespace: planescape-system
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: planescape-operator
subjects:
  - kind: ServiceAccount
    name: planescape-operator
    namespace: planescape-system
```

#### Helm-Based Deployment

```yaml
# values.yaml
rbac:
  create: true
  rules:
    - apiGroups: [""]
      resources: ["pods", "services", "secrets"]
      verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
    - apiGroups: ["planescape.io"]
      resources: ["planescapestacks", "planescapejobs"]
      verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
```

### Vault Authentication

#### Operator-Based Deployment

```yaml
spec:
  components:
    vault:
      server:
        auth:
          kubernetes:
            enabled: true
            path: kubernetes
            role: planescape
            policies:
              - postgres-dynamic
              - jenkins-auth
```

#### Helm-Based Deployment

```yaml
# values.yaml
vault:
  server:
    auth:
      kubernetes:
        enabled: true
        path: kubernetes
        role: planescape
        policies:
          - postgres-dynamic
          - jenkins-auth
```

## Secret Management

### Vault Secrets

#### Operator-Based Deployment

```yaml
spec:
  components:
    vault:
      server:
        secrets:
          - path: database/config/postgresql
            type: database
            config:
              plugin_name: postgresql-database-plugin
              connection_url: "postgresql://{{username}}:{{password}}@postgresql:5432/postgres"
              allowed_roles: ["postgres"]
          - path: auth/jenkins/role/jenkins
            type: jwt
            config:
              role_type: jwt
              bound_audiences: ["jenkins"]
              user_claim: "sub"
```

#### Helm-Based Deployment

```yaml
# values.yaml
vault:
  server:
    secrets:
      database:
        postgresql:
          path: database/config/postgresql
          type: database
          config:
            plugin_name: postgresql-database-plugin
            connection_url: "postgresql://{{username}}:{{password}}@postgresql:5432/postgres"
            allowed_roles: ["postgres"]
      auth:
        jenkins:
          path: auth/jenkins/role/jenkins
          type: jwt
          config:
            role_type: jwt
            bound_audiences: ["jenkins"]
            user_claim: "sub"
```

### Secret Rotation

#### Operator-Based Deployment

```yaml
spec:
  components:
    vault:
      server:
        secrets:
          rotation:
            enabled: true
            interval: 24h
            max_ttl: 168h
```

#### Helm-Based Deployment

```yaml
# values.yaml
vault:
  server:
    secrets:
      rotation:
        enabled: true
        interval: 24h
        max_ttl: 168h
```

## TLS Configuration

### Certificate Management

#### Operator-Based Deployment

```yaml
spec:
  components:
    vault:
      server:
        tls:
          enabled: true
          secretName: vault-tls
          certManager:
            enabled: true
            issuer: vault-issuer
    postgresql:
      tls:
        enabled: true
        secretName: postgresql-tls
        certManager:
          enabled: true
          issuer: postgresql-issuer
    jenkins:
      controller:
        tls:
          enabled: true
          secretName: jenkins-tls
          certManager:
            enabled: true
            issuer: jenkins-issuer
```

#### Helm-Based Deployment

```yaml
# values.yaml
vault:
  server:
    tls:
      enabled: true
      secretName: vault-tls
      certManager:
        enabled: true
        issuer: vault-issuer
postgresql:
  tls:
    enabled: true
    secretName: postgresql-tls
    certManager:
      enabled: true
      issuer: postgresql-issuer
jenkins:
  controller:
    tls:
      enabled: true
      secretName: jenkins-tls
      certManager:
        enabled: true
        issuer: jenkins-issuer
```

## Security Hardening

### Pod Security

#### Operator-Based Deployment

```yaml
spec:
  components:
    vault:
      server:
        securityContext:
          runAsNonRoot: true
          runAsUser: 100
          runAsGroup: 1000
          fsGroup: 1000
        podSecurityContext:
          allowPrivilegeEscalation: false
          capabilities:
            drop:
              - ALL
    postgresql:
      primary:
        securityContext:
          runAsNonRoot: true
          runAsUser: 1001
          runAsGroup: 1001
          fsGroup: 1001
        podSecurityContext:
          allowPrivilegeEscalation: false
          capabilities:
            drop:
              - ALL
    jenkins:
      controller:
        securityContext:
          runAsNonRoot: true
          runAsUser: 1000
          runAsGroup: 1000
          fsGroup: 1000
        podSecurityContext:
          allowPrivilegeEscalation: false
          capabilities:
            drop:
              - ALL
```

#### Helm-Based Deployment

```yaml
# values.yaml
vault:
  server:
    securityContext:
      runAsNonRoot: true
      runAsUser: 100
      runAsGroup: 1000
      fsGroup: 1000
    podSecurityContext:
      allowPrivilegeEscalation: false
      capabilities:
        drop:
          - ALL
postgresql:
  primary:
    securityContext:
      runAsNonRoot: true
      runAsUser: 1001
      runAsGroup: 1001
      fsGroup: 1001
    podSecurityContext:
      allowPrivilegeEscalation: false
      capabilities:
        drop:
          - ALL
jenkins:
  controller:
    securityContext:
      runAsNonRoot: true
      runAsUser: 1000
      runAsGroup: 1000
      fsGroup: 1000
    podSecurityContext:
      allowPrivilegeEscalation: false
      capabilities:
        drop:
          - ALL
```

### Resource Limits

#### Operator-Based Deployment

```yaml
spec:
  components:
    vault:
      server:
        resources:
          requests:
            cpu: "100m"
            memory: "256Mi"
          limits:
            cpu: "500m"
            memory: "512Mi"
    postgresql:
      primary:
        resources:
          requests:
            cpu: "200m"
            memory: "512Mi"
          limits:
            cpu: "1000m"
            memory: "1Gi"
    jenkins:
      controller:
        resources:
          requests:
            cpu: "200m"
            memory: "512Mi"
          limits:
            cpu: "1000m"
            memory: "1Gi"
```

#### Helm-Based Deployment

```yaml
# values.yaml
vault:
  server:
    resources:
      requests:
        cpu: "100m"
        memory: "256Mi"
      limits:
        cpu: "500m"
        memory: "512Mi"
postgresql:
  primary:
    resources:
      requests:
        cpu: "200m"
        memory: "512Mi"
      limits:
        cpu: "1000m"
        memory: "1Gi"
jenkins:
  controller:
    resources:
      requests:
        cpu: "200m"
        memory: "512Mi"
      limits:
        cpu: "1000m"
        memory: "1Gi"
```

## Security Best Practices

1. **Regular Updates**
   - Keep all components updated
   - Monitor security advisories
   - Apply security patches promptly

2. **Audit Logging**
   - Enable Vault audit logs
   - Monitor Kubernetes audit logs
   - Review access patterns

3. **Access Control**
   - Use least privilege principle
   - Regular access reviews
   - Rotate credentials regularly

4. **Network Security**
   - Use network policies
   - Enable TLS everywhere
   - Restrict service access

5. **Secret Management**
   - Use Vault for all secrets
   - Enable automatic rotation
   - Audit secret access

6. **Monitoring**
   - Monitor security events
   - Set up alerts
   - Regular security scans

## Getting Help

For security-related issues:

1. Check the [Security Policy](SECURITY.md)
2. Report vulnerabilities to security@planescape.io
3. Join our [Security Channel](https://slack.planescape.io/security)
4. Review [Security Advisories](https://github.com/your-org/planescape/security/advisories) 