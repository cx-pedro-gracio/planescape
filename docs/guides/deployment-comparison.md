# Deployment Approaches Comparison

This guide compares the Helm-based and operator-based deployment approaches in Planescape, helping you understand when and why to use each method.

## Overview

Planescape supports two deployment approaches:

1. **Helm-based Deployment**
   - Uses Helm charts for declarative configuration
   - Simpler to get started
   - Familiar to Kubernetes users
   - Good for standard deployments

2. **Operator-based Deployment**
   - Uses a custom Kubernetes operator
   - More powerful automation
   - Better for complex scenarios
   - Provides custom resources and controllers

## When to Use Each Approach

### Use Helm When:
- You need a quick start
- Your deployment is relatively standard
- You prefer declarative configuration
- You want to use existing Helm tools and workflows
- You need to integrate with other Helm charts

### Use Operator When:
- You need custom automation
- You want to extend functionality
- You need complex reconciliation logic
- You want to implement custom controllers
- You need to manage custom resources

## Architecture Comparison

### Helm Architecture
```
┌─────────────────┐
│   Helm Chart    │
├─────────────────┤
│  - Templates    │
│  - Values       │
│  - Dependencies │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Kubernetes     │
│  Resources      │
└─────────────────┘
```

### Operator Architecture
```
┌─────────────────┐
│  Custom         │
│  Resources      │
├─────────────────┤
│  - Stack        │
│  - Job          │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│   Operator      │
├─────────────────┤
│  - Controllers  │
│  - Services     │
│  - Webhooks     │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Kubernetes     │
│  Resources      │
└─────────────────┘
```

## Implementation Examples

### Helm-based Deployment

```yaml
# values.yaml
vault:
  enabled: true
  server:
    ha:
      enabled: true
    tls:
      enabled: true

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
```

```bash
# Installation
helm install planescape planescape/charts \
  --namespace planescape-system \
  -f values.yaml
```

### Operator-based Deployment

```yaml
# stack.yaml
apiVersion: planescape.io/v1alpha1
kind: PlanescapeStack
metadata:
  name: my-stack
spec:
  components:
    vault:
      enabled: true
      server:
        ha:
          enabled: true
        tls:
          enabled: true
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
```

```bash
# Installation
kubectl apply -f stack.yaml
```

## Key Differences

### Configuration
- **Helm**: Uses values.yaml and templates
- **Operator**: Uses custom resources and controllers

### Automation
- **Helm**: Static configuration, manual updates
- **Operator**: Dynamic reconciliation, automatic updates

### Extensibility
- **Helm**: Limited to template variables
- **Operator**: Full programmatic control

### Learning Curve
- **Helm**: Easier to learn, familiar to Kubernetes users
- **Operator**: Steeper learning curve, requires Go knowledge

## Best Practices

### Helm Best Practices
1. Use value files for different environments
2. Leverage Helm hooks for lifecycle management
3. Use templates for reusable components
4. Document all configurable values

### Operator Best Practices
1. Follow Kubernetes operator patterns
2. Implement proper error handling
3. Use finalizers for cleanup
4. Add comprehensive logging
5. Implement health checks

## Learning Resources

### Helm Learning
1. [Helm Documentation](https://helm.sh/docs/)
2. [Helm Best Practices](https://helm.sh/docs/chart_best_practices/)
3. [Helm Chart Template Guide](https://helm.sh/docs/chart_template_guide/)

### Operator Learning
1. [Kubernetes Operator Pattern](https://kubernetes.io/docs/concepts/extend-kubernetes/operator/)
2. [Operator SDK Documentation](https://sdk.operatorframework.io/)
3. [Kubebuilder Book](https://book.kubebuilder.io/)

## Migration Between Approaches

### Helm to Operator
1. Create custom resources based on Helm values
2. Implement controllers for resource reconciliation
3. Migrate templates to Go code
4. Update deployment processes

### Operator to Helm
1. Extract configuration to values.yaml
2. Create Helm templates from custom resources
3. Implement Helm hooks for automation
4. Update deployment processes

## Conclusion

Both approaches have their place in the Planescape ecosystem:
- Use Helm for simpler, standard deployments
- Use the operator for complex, automated scenarios
- Consider your team's expertise and requirements
- Both approaches can be used together

## Next Steps

1. Review the [Deployment Guide](deployment.md) for detailed instructions
2. Check the [Security Guide](security.md) for security considerations
3. Read the [Troubleshooting Guide](troubleshooting.md) for common issues
4. Explore the [Examples](../examples/) directory for sample configurations 