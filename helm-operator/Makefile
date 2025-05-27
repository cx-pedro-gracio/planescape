# Makefile for Planescape Helm Operator

# Image URL to use all building/pushing image targets
IMG ?= ghcr.io/planescape/helm-operator:latest

# Kubernetes namespace
NAMESPACE ?= planescape-system

# Get the currently used golang install path (in GOPATH/bin, unless GOBIN is set)
ifeq (,$(shell go env GOBIN))
GOBIN=$(shell go env GOPATH)/bin
else
GOBIN=$(shell go env GOBIN)
endif

.PHONY: help
help: ## Display this help.
	@awk 'BEGIN {FS = ":.*##"; printf "\nUsage:\n  make \033[36m<target>\033[0m\n"} /^[a-zA-Z_0-9-]+:.*?##/ { printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2 } /^##@/ { printf "\n\033[1m%s\033[0m\n", substr($$0, 5) } ' $(MAKEFILE_LIST)

##@ Development

.PHONY: lint
lint: ## Lint Helm charts
	helm lint ../charts/
	helm lint ../charts/planescape-job/

.PHONY: template
template: ## Template Helm charts for validation
	helm template test ../charts/ --values ../charts/envs/local/values.yaml
	helm template test-job ../charts/planescape-job/

.PHONY: validate
validate: lint template ## Validate all charts and configurations
	kubectl --dry-run=client apply -f config/crd/bases/
	kubectl --dry-run=client apply -f ../examples/stacks/demo-stack-helm.yaml
	kubectl --dry-run=client apply -f ../examples/jobs/demo-job-helm.yaml

##@ Build

.PHONY: docker-build
docker-build: ## Build docker image with the operator.
	docker build -t ${IMG} .

.PHONY: docker-push
docker-push: ## Push docker image with the operator.
	docker push ${IMG}

.PHONY: docker-buildx
docker-buildx: ## Build and push docker image for multiple platforms.
	docker buildx build --platform linux/amd64,linux/arm64 -t ${IMG} --push .

##@ Deployment

.PHONY: install-crds
install-crds: ## Install CRDs into the K8s cluster specified in ~/.kube/config.
	kubectl apply -f config/crd/bases/

.PHONY: uninstall-crds
uninstall-crds: ## Uninstall CRDs from the K8s cluster specified in ~/.kube/config.
	kubectl delete -f config/crd/bases/

.PHONY: deploy
deploy: install-crds ## Deploy operator to the K8s cluster specified in ~/.kube/config.
	kubectl apply -f config/rbac/role.yaml
	kubectl apply -f config/manager/manager.yaml

.PHONY: undeploy
undeploy: ## Undeploy operator from the K8s cluster specified in ~/.kube/config.
	kubectl delete -f config/manager/manager.yaml
	kubectl delete -f config/rbac/role.yaml

.PHONY: deploy-examples
deploy-examples: ## Deploy example resources
	kubectl apply -f ../examples/stacks/demo-stack-helm.yaml
	kubectl apply -f ../examples/jobs/demo-job-helm.yaml

.PHONY: undeploy-examples
undeploy-examples: ## Remove example resources
	kubectl delete -f ../examples/stacks/demo-stack-helm.yaml --ignore-not-found=true
	kubectl delete -f ../examples/jobs/demo-job-helm.yaml --ignore-not-found=true

##@ Utilities

.PHONY: logs
logs: ## Show operator logs
	kubectl logs -f deployment/planescape-helm-operator -n $(NAMESPACE)

.PHONY: status
status: ## Show operator status
	kubectl get pods -n $(NAMESPACE) -l app.kubernetes.io/name=planescape-helm-operator
	kubectl get planescapestacks -A
	kubectl get planescapejobs -A

.PHONY: clean
clean: undeploy-examples undeploy uninstall-crds ## Clean up all resources

.PHONY: restart
restart: ## Restart the operator
	kubectl rollout restart deployment/planescape-helm-operator -n $(NAMESPACE)

##@ Testing

.PHONY: test-stack
test-stack: ## Test stack creation
	kubectl apply -f ../examples/stacks/demo-stack-helm.yaml
	kubectl wait --for=condition=Ready planescapestack/demo-stack -n $(NAMESPACE) --timeout=600s

.PHONY: test-job
test-job: ## Test job creation
	kubectl apply -f ../examples/jobs/demo-job-helm.yaml
	kubectl wait --for=condition=Complete job -l app=demo-job -n $(NAMESPACE) --timeout=300s

.PHONY: test-all
test-all: test-stack test-job ## Run all tests