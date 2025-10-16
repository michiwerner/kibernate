#!/usr/bin/env bash
# Ensure clean shebang (no BOM)

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service kibernate 2>/dev/null || true
  kubectl delete service kibernate-test 2>/dev/null || true
}
trap finally EXIT

echo "=== Testing Multi-Instance Kibernate Configuration (Host Header Selection) ==="

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

# Install Kibernate with host-header multi-instance config
echo "Installing Kibernate with host-header multi-instance configuration..."
./scripts/install-helm-chart.sh -f ./configs/tests/helm/07-test-multi-instance-host-header-values.yml

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

# Wait for kibernate pod to be ready
echo "Waiting for kibernate pod to be ready..."
kubectl wait --for=condition=ready --timeout=60s pod -l app.kubernetes.io/name=kibernate

# Create a shorter-named alias service and wait for endpoints (more robust DNS & readiness)
kubectl expose deployment kibernate --name=kibernate-test --port=8080 --target-port=8080 || true

# Wait for the kibernate-test service to be created
until kubectl get service kibernate-test &> /dev/null; do
  echo "Waiting for kibernate-test service to be created..."
  sleep 2
done

# Wait for endpoints to have addresses (kibernate-test)
echo "Waiting for kibernate-test service endpoints to be ready..."
for i in {1..30}; do
  if kubectl get endpoints kibernate-test &> /dev/null && \
     [ "$(kubectl get endpoints kibernate-test -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null)" ]; then
    echo "Endpoints are ready"
    break
  fi
  echo "Waiting for endpoints... (attempt $i/30)"
  sleep 2
done

# Additional wait for DNS propagation
sleep 5

# Helper to run a curl with a specific Host header and retry
function curl_with_host() {
  local host="$1"
  kubectl run -i --rm curl-$(echo "$host" | tr '.' '-') --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
set -eo pipefail
sleep 5
i=1
while [ \$i -le 5 ]; do
  echo \"Attempt \$i/5 to connect to kibernate-test:8080 with Host: ${host}\"
  if curl -f --connect-timeout 10 --max-time 30 -H \"Host: ${host}\" 'http://kibernate-test:8080' 2>/dev/null | tee > /tmp/curl_out.txt; then
    echo
    if grep -q 'Thank you for using nginx.' /tmp/curl_out.txt; then
      echo \"Test successful!\"
      exit 0
    else
      echo \"Response received but content doesn't match expected pattern\"
    fi
  else
    echo \"Attempt \$i failed, waiting before retry...\"
    sleep 5
  fi
  i=\$((i+1))
done
echo \"All attempts failed\"
exit 1
  "
}

# Test instance 1 via Host header
echo "Testing instance 1 via Host header app1.local on port 8080..."
curl_with_host app1.local

# Test instance 2 via Host header
echo "Testing instance 2 via Host header app2.local on port 8080..."
curl_with_host app2.local

# Test instance 3 via Host header
echo "Testing instance 3 via Host header app3.local on port 8080..."
curl_with_host app3.local

echo "=== Multi-Instance Host Header Test Completed Successfully ==="
