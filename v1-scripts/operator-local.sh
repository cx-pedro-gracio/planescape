#!/bin/bash

# Run Planescape operator locally for development
# This script sets up the environment and runs the operator in development mode

set -euo pipefail

# Set up .NET environment
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

# Set operator environment variables
export WATCH_NAMESPACE="${WATCH_NAMESPACE:-default}"
export POD_NAME="planescape-operator-local"
export VAULT_LOCAL_PORT_FORWARD="${VAULT_LOCAL_PORT_FORWARD:-true}"

# Change to operator directory
cd "$(dirname "$0")/../operator"

# Run the operator
dotnet run 