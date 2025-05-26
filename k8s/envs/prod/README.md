# Production Environment

This directory contains the configuration overrides for running the application in production.

## Purpose
- Production deployment
- High availability
- Security and performance optimization

## Key Differences from Local
1. **Resource Limits**
   - Higher CPU and memory limits
   - Larger storage volumes
   - Full plugin set for Jenkins

2. **Access**
   - ClusterIP services
   - Strict network policies
   - Production ingress configuration

3. **Features**
   - Enabled metrics and monitoring
   - Automated backups
   - Production-grade health checks
   - High availability configuration

## Deployment Process

1. **Prerequisites**
   - Production Kubernetes cluster
   - Production storage class
   - Production ingress controller
   - Secret management system

2. **Create Namespaces**
   ```bash
   kubectl create namespace planescape
   kubectl create namespace jenkins-workers
   ```

3. **Configure Secrets**
   - Use your organization's secret management system
   - Never store secrets in Git
   - Required secrets:
     - `postgres-secret`
     - `jenkins-secret`

4. **Deploy Components**
   ```bash
   # PostgreSQL
   helm upgrade --install postgres bitnami/postgresql \
     --namespace planescape \
     -f ../../base/postgres/values.yaml \
     -f postgres.yaml
   
   # Jenkins
   helm upgrade --install jenkins jenkins/jenkins \
     --namespace planescape \
     -f ../../base/jenkins/values.yaml \
     -f jenkins.yaml
   ```

## Monitoring

1. **PostgreSQL**
   - Prometheus metrics
   - ServiceMonitor for Prometheus
   - Health check alerts
   - Backup status monitoring

2. **Jenkins**
   - Resource usage monitoring
   - Worker pod monitoring
   - Backup status monitoring
   - Health check alerts

## Maintenance

1. **Secret Rotation**
   - Regular password rotation
   - Use secret management system
   - Coordinate with team

2. **Backups**
   - Daily automated backups
   - 7-day retention
   - Regular backup testing

3. **Updates**
   - Regular security updates
   - Plugin updates
   - Chart version updates

## Security

1. **Network Policies**
   - Strict ingress/egress rules
   - Namespace isolation
   - Service mesh integration (if applicable)

2. **Access Control**
   - RBAC configuration
   - Service accounts
   - Network policies

3. **Monitoring**
   - Security scanning
   - Audit logging
   - Alert configuration

## Troubleshooting

1. **Component Status**
   ```bash
   # PostgreSQL
   kubectl get pods -n planescape -l app=postgres
   kubectl logs -n planescape -l app=postgres
   
   # Jenkins
   kubectl get pods -n planescape -l app=jenkins
   kubectl get pods -n jenkins-workers
   ```

2. **Storage**
   ```bash
   kubectl get pvc -n planescape
   ```

3. **Metrics**
   ```bash
   # Check Prometheus metrics
   kubectl port-forward -n monitoring svc/prometheus-server 9090:9090
   ```

## Emergency Procedures

1. **Component Failure**
   - Check logs and metrics
   - Verify network policies
   - Check storage status
   - Contact on-call if needed

2. **Data Recovery**
   - Use latest backup
   - Follow recovery procedures
   - Document incident

3. **Security Incident**
   - Follow security incident response plan
   - Rotate affected secrets
   - Document incident 