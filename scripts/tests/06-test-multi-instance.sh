#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service kibernate 2>/dev/null || true
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
./scripts/install-helm-chart.sh -f ./configs/tests/helm/06-test-multi-instance-values.yml

# Wait for all deployments to be ready
echo "Waiting for deployments to be ready..."
kubectl wait --for=condition=available --timeout=60s deployment/testtarget1
kubectl wait --for=condition=available --timeout=60s deployment/testtarget2
kubectl wait --for=condition=available --timeout=60s deployment/testtarget3
kubectl wait --for=condition=available --timeout=60s deployment/kibernate

# Wait for target pods to actually be running and ready
echo "Waiting for target pods to be ready..."
kubectl wait --for=condition=ready --timeout=60s pod -l app=testtarget1
kubectl wait --for=condition=ready --timeout=60s pod -l app=testtarget2
kubectl wait --for=condition=ready --timeout=60s pod -l app=testtarget3

# Wait for kibernate service to be ready
echo "Waiting for kibernate service endpoints to be ready..."
kubectl wait --for=condition=ready --timeout=60s endpoints/kibernate || {
  echo "Kibernate endpoints not ready, checking what's wrong..."
  kubectl get endpoints kibernate
  kubectl get pods -l app.kubernetes.io/name=kibernate
  kubectl describe service kibernate
  exit 1
}

# Test instance 1 (port 8080)
echo "Testing instance 1 on port 8080..."
kubectl run -i --rm test1 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
curl -f --connect-timeout 10 --max-time 30 'http://kibernate:8080' | grep -q 'Thank you for using nginx.'
"

# Test instance 2 (port 8081)
echo "Testing instance 2 on port 8081..."
kubectl run -i --rm test2 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
curl -f --connect-timeout 10 --max-time 30 'http://kibernate:8081' | grep -q 'Thank you for using nginx.'
"

# Test instance 3 (port 8082)
echo "Testing instance 3 on port 8082..."
kubectl run -i --rm test3 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
curl -f --connect-timeout 10 --max-time 30 'http://kibernate:8082' | grep -q 'Thank you for using nginx.'
"

echo "=== Multi-Instance Test Completed Successfully ==="