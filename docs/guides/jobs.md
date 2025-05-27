# Job Management Guide

This guide covers how to create, manage, and monitor jobs using the PlanescapeJob custom resource.

## Overview

PlanescapeJob provides a unified interface for managing both one-time and recurring jobs in Kubernetes, with built-in Vault integration for secure credential management.

## Job Types

### One-time Jobs

One-time jobs run immediately and only once:

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeJob
metadata:
  name: migration-job
  namespace: planescape-system
spec:
  schedule: "@once"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: migrate
            image: migrate/migrate:v4.15.2
            command: ["migrate"]
            args: ["-path", "/migrations", "-database", "postgres://...", "up"]
          restartPolicy: Never
```

### Scheduled Jobs (CronJobs)

Recurring jobs use cron expressions:

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeJob
metadata:
  name: daily-cleanup
  namespace: planescape-system
spec:
  schedule: "0 2 * * *"  # Daily at 2 AM
  concurrencyPolicy: Forbid
  successfulJobsHistoryLimit: 7
  failedJobsHistoryLimit: 3
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: cleanup
            image: alpine:3.18
            command: ["/bin/sh"]
            args: ["-c", "echo 'Cleaning up...'"]
          restartPolicy: OnFailure
```

## Vault Integration

### Automatic Secret Injection

Jobs can automatically receive secrets from Vault using annotations:

```yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeJob
metadata:
  name: database-job
spec:
  schedule: "@once"
  jobTemplate:
    spec:
      template:
        metadata:
          annotations:
            vault.hashicorp.com/agent-inject: "true"
            vault.hashicorp.com/role: "database-reader"
            vault.hashicorp.com/agent-inject-secret-db: "database/creds/readonly"
            vault.hashicorp.com/agent-inject-template-db: |
              {{- with secret "database/creds/readonly" -}}
              export PGUSER="{{ .Data.username }}"
              export PGPASSWORD="{{ .Data.password }}"
              {{- end -}}
        spec:
          containers:
          - name: db-query
            image: postgres:15-alpine
            command: ["/bin/sh"]
            args:
            - -c
            - |
              source /vault/secrets/db
              psql -h postgresql -d planescape -c "SELECT COUNT(*) FROM users;"
```

### Dynamic Database Credentials

For database operations, use dynamic credentials:

```yaml
env:
- name: PGHOST
  value: "postgresql.planescape-system.svc.cluster.local"
- name: PGDATABASE
  value: "planescape"
- name: PGUSER
  valueFrom:
    secretKeyRef:
      name: vault-db-creds
      key: username
- name: PGPASSWORD
  valueFrom:
    secretKeyRef:
      name: vault-db-creds
      key: password
```

## Job Configuration

### Resource Management

Always specify resource requests and limits:

```yaml
containers:
- name: worker
  image: myapp:latest
  resources:
    requests:
      cpu: "100m"
      memory: "128Mi"
    limits:
      cpu: "500m"
      memory: "512Mi"
```

### Concurrency Control

Control how concurrent executions are handled:

- `Allow`: Allow concurrent executions (default for one-time jobs)
- `Forbid`: Skip new execution if previous is still running
- `Replace`: Cancel previous execution and start new one

```yaml
spec:
  concurrencyPolicy: Forbid
```

### History Management

Control how many completed jobs to retain:

```yaml
spec:
  successfulJobsHistoryLimit: 3  # Keep 3 successful jobs
  failedJobsHistoryLimit: 1      # Keep 1 failed job
```

## Monitoring and Troubleshooting

### Check Job Status

```bash
# List all jobs
kubectl get planescapejobs -n planescape-system

# Get detailed status
kubectl describe planescapejob my-job -n planescape-system

# Check job execution history
kubectl get jobs -l job-name=my-job -n planescape-system
```

### View Logs

```bash
# Get logs from the most recent job execution
kubectl logs -l job-name=my-job -n planescape-system

# Get logs from a specific job pod
kubectl logs my-job-12345-abcde -n planescape-system
```

### Common Issues

1. **Job not starting**: Check RBAC permissions and resource quotas
2. **Vault secrets not available**: Verify Vault role and policies
3. **Database connection failed**: Check network policies and credentials

## Best Practices

1. **Use specific image tags** instead of `latest`
2. **Set appropriate resource limits** to prevent resource exhaustion
3. **Use Vault for all secrets** instead of hardcoded values
4. **Implement proper error handling** in job scripts
5. **Use meaningful names and labels** for easier management
6. **Test jobs in development** before deploying to production

## Examples

See the [examples directory](../examples/jobs/) for more job configurations:

- [One-time job](../examples/jobs/one-time-job.yaml)
- [Cron job](../examples/jobs/cron-job.yaml)
- [Demo job with Vault](../examples/jobs/demo-job.yaml) 