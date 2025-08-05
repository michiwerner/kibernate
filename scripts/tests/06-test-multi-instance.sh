#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service kibernate-instance1 kibernate-instance2 kibernate-instance3 2>/dev/null || true
}
trap finally EXIT

echo "=== Testing Multi-Instance Kibernate Configuration ==="

# Create three test deployments
echo "Creating test deployments..."
kubectl create deployment testtarget1 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl create deployment testtarget2 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl create deployment testtarget3 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080

# Expose the deployments as services
echo "Creating services..."
kubectl expose deployment testtarget1 --port=8080 --target-port=8080
kubectl expose deployment testtarget2 --port=8080 --target-port=8080
kubectl expose deployment testtarget3 --port=8080 --target-port=8080

# Install Kibernate with multi-instance config
echo "Installing Kibernate with multi-instance configuration..."
./scripts/install-helm-chart.sh -f ./configs/tests/helm/06-test-multi-instance.yml

# Wait for all deployments to be ready
echo "Waiting for deployments to be ready..."
kubectl wait --for=condition=available --timeout=60s deployment/testtarget1
kubectl wait --for=condition=available --timeout=60s deployment/testtarget2
kubectl wait --for=condition=available --timeout=60s deployment/testtarget3
kubectl wait --for=condition=available --timeout=60s deployment/kibernate

# Create services for each Kibernate instance port
echo "Creating services for Kibernate instances..."
kubectl expose deployment kibernate --name=kibernate-instance1 --port=8080 --target-port=8080 || true
kubectl expose deployment kibernate --name=kibernate-instance2 --port=8081 --target-port=8081 || true
kubectl expose deployment kibernate --name=kibernate-instance3 --port=8082 --target-port=8082 || true

# Test instance 1 (port 8080)
echo "Testing instance 1 on port 8080..."
kubectl run -i --rm test1 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "set -eo pipefail; sleep 10; curl 'http://kibernate-instance1:8080' | tee > /tmp/curl_out.txt; echo; cat /tmp/curl_out.txt | grep 'Thank you for using nginx.'"

# Test instance 2 (port 8081)
echo "Testing instance 2 on port 8081..."
kubectl run -i --rm test2 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "set -eo pipefail; sleep 5; curl 'http://kibernate-instance2:8081' | tee > /tmp/curl_out.txt; echo; cat /tmp/curl_out.txt | grep 'Thank you for using nginx.'"

# Test instance 3 (port 8082)
echo "Testing instance 3 on port 8082..."
kubectl run -i --rm test3 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "set -eo pipefail; sleep 5; curl 'http://kibernate-instance3:8082' | tee > /tmp/curl_out.txt; echo; cat /tmp/curl_out.txt | grep 'Thank you for using nginx.'"

echo "=== Multi-Instance Test Completed Successfully ==="