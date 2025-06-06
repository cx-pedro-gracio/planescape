# Planescape Umbrella Chart

## Overview

Planescape is a Kubernetes platform that deploys PostgreSQL, Jenkins, and Vault using official Helm charts, with dynamic database credentials managed by Vault. Jenkins jobs can securely log to the database using short-lived credentials provisioned at runtime.

---

## Architecture

- **PostgreSQL**: Deployed via Bitnami's official Helm chart. Used as the main database.
- **Jenkins**: Deployed via the official Jenkins Helm chart. A Job DSL script creates a job that logs messages and timestamps to PostgreSQL.
- **Vault**: Deployed via the official HashiCorp Vault Helm chart. Configured to issue dynamic, short-lived Postgres credentials for Jenkins jobs.
- **Dynamic Credentials**: Vault is automatically configured (via a Kubernetes Job) to issue dynamic database credentials. Jenkins fetches these at build time using the Vault plugin.

---

## Deployment

### Prerequisites
- Kubernetes cluster
- Helm 3.x

### Steps
1. **Update dependencies:**
   ```sh
   helm dependency update charts/planescape
   ```
2. **Install the chart:**
   ```sh
   helm install planescape charts/planescape -n planescape --create-namespace
   ```

---

## How It Works

1. **Vault Initialization**
   - A Kubernetes Job (`vault-init-job.yaml`) waits for Vault and Postgres to be ready, then configures Vault's database secrets engine and a role for Jenkins.
   - Vault uses the Postgres admin credentials (from `values.yaml`) to create dynamic users.

2. **Jenkins Job Creation**
   - A ConfigMap (`jenkins-jobdsl-configmap.yaml`) contains a Job DSL script that defines a Jenkins job (`LogToPostgres`).
   - Jenkins is configured (via `values.yaml` and `initScripts`) to create a seed job that runs this DSL script.

3. **Dynamic Credentials**
   - When the Jenkins job runs, it uses the Vault plugin to fetch a fresh Postgres username/password from Vault (`database/creds/jenkins-role`).
   - The job logs a message and timestamp to the database using these credentials.

---

## File Structure

```
charts/planescape/
  Chart.yaml                # Umbrella chart definition
  values.yaml               # Main configuration (Postgres, Jenkins, Vault)
  templates/
    jenkins-jobdsl-configmap.yaml  # Jenkins Job DSL script
    vault-init-job.yaml            # Vault DB secrets engine setup
```

---

## Customization
- **Postgres admin credentials**: Set in `values.yaml` under `postgres.auth`. Used only by Vault to create dynamic users.
- **Jenkins admin credentials**: Set in `values.yaml` under `jenkins.controller.admin.username` and `adminPassword`.
- **Vault plugin configuration**: Jenkins is pre-configured to use the Vault plugin for dynamic DB credentials.

---

## Security Notes
- Jenkins never sees or stores static DB passwords.
- All DB access from Jenkins uses short-lived, least-privilege credentials issued by Vault at build time.
- The only static DB password is the Postgres admin password, used by Vault to create dynamic users.

---

## Troubleshooting
- Ensure all pods (Vault, Postgres, Jenkins) are running and ready before running jobs.
- Check the logs of the `vault-init` job if dynamic credential setup fails.
- For production, configure Vault with a secure root token and proper authentication.

---

## License
MIT 