#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget 2>/dev/null || true
  kubectl delete service testtarget 2>/dev/null || true
}
trap finally EXIT

echo "=== Testing Backward Compatibility with Old Config Format ==="

# Create test deployment
echo "Creating test deployment..."
kubectl create deployment testtarget --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080

# Install Kibernate with old config format (should be auto-converted)
echo "Installing Kibernate with old config format..."
./scripts/install-helm-chart.sh -f ./configs/testing.yml

# Wait for deployments to be ready
echo "Waiting for deployments to be ready..."
kubectl wait --for=condition=available --timeout=60s deployment/testtarget
kubectl wait --for=condition=available --timeout=60s deployment/kibernate

# Test that it works with the old config format
echo "Testing Kibernate with old config format..."
kubectl run -i --rm test --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "set -eo pipefail; sleep 10; curl 'http://kibernate:8080' | tee > /tmp/curl_out.txt; echo; cat /tmp/curl_out.txt | grep 'Thank you for using nginx.'"

echo "=== Backward Compatibility Test Completed Successfully ==="