FROM quay.io/operator-framework/helm-operator:v1.32.0

# Copy our Helm charts into the operator image
COPY charts/ /opt/helm/charts/

# Copy watches configuration
COPY watches.yaml /opt/helm/watches.yaml

# Set proper permissions
USER 65532:65532

# Labels for better image management
LABEL org.opencontainers.image.title="Planescape Helm Operator"
LABEL org.opencontainers.image.description="Kubernetes operator for managing Planescape stacks using Helm"
LABEL org.opencontainers.image.vendor="Planescape"
LABEL org.opencontainers.image.licenses="Apache-2.0"
LABEL org.opencontainers.image.source="https://github.com/planescape/planescape" 