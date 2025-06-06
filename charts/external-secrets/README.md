# External Secrets Operator Chart

This chart deploys External Secrets Operator (ESO) for the Planescape platform.

## Purpose

ESO synchronizes secrets from external secret management systems (like Vault) into Kubernetes secrets. This chart must be deployed **before** the main Planescape chart.

## Deployment Order

1. **First**: Deploy this chart to install ESO and its CRDs
2. **Then**: Deploy the main `planescape` chart

## Installation

```bash
# 1. Install External Secrets Operator
helm upgrade --install external-secrets ./charts/external-secrets/ -n planescape --create-namespace

# 2. Wait for ESO to be ready
kubectl wait --for=condition=available deployment/external-secrets -n planescape --timeout=300s

# 3. Install main Planescape chart
helm upgrade --install planescape ./charts/planescape/ -n planescape
```

## Components

- External Secrets Operator with CRDs
- ServiceAccount for ESO
- Webhook for validating external secrets 