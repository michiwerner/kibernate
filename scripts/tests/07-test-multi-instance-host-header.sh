#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../ || exit 1

function finally() {
  exit_code=${1:-0}
  set +eo pipefail

  if [ "$exit_code" != "0" ]; then
    echo "==== DEBUG: Collecting pod logs because the script failed (exit code $exit_code) ===="
    echo "-- kibernate pods --"
    kubectl get pods -l app.kubernetes.io/name=kibernate -o wide || true
    kubectl describe deployment kibernate || true
    kubectl get pods -l app.kubernetes.io/name=kibernate -o name 2>/dev/null | while IFS= read -r p; do
      echo "--- logs for $p ---"
      kubectl logs "$p" --tail=200 || true
    done

    echo "-- target pods --"
    for sel in "app=testtarget1" "app=testtarget2" "app=testtarget3"; do
      kubectl get pods -l "$sel" -o wide || true
      kubectl get pods -l "$sel" -o name 2>/dev/null | while IFS= read -r p; do
        echo "--- logs for $p ---"
        kubectl logs "$p" --tail=200 || true
      done
    done
    echo "==== END DEBUG LOGS ===="
  fi

  kubectl delete deployment testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service kibernate 2>/dev/null || true
  kubectl delete service kibernate-test 2>/dev/null || true
  exit "$exit_code"
}
trap 'finally $?' EXIT

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

# Generate a unique token
function _gen_token() {
  date +%s%N-$RANDOM
}

# Helper to run a curl with a specific Host header and a unique token in the query string.
function curl_with_host_and_token() {
  local host="$1"
  local token="$2"
  kubectl run -i --rm "curl-$(echo "$host" | tr '.' '-')-$token" --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
set -eo pipefail
sleep 5
i=1
while [ \$i -le 5 ]; do
  echo \"Attempt \$i/5 to connect to kibernate-test:8080 with Host: ${host} and token: ${token}\"
  if curl -f --connect-timeout 10 --max-time 30 -H \"Host: ${host}\" \"http://kibernate-test:8080/?token=${token}\" 2>/dev/null | tee /tmp/curl_out.txt; then
    echo
    if grep -q 'Thank you for using nginx.' /tmp/curl_out.txt; then
      echo \"Request succeeded\"
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

# Verify that the request identified by token only hit the expected backend service
function assert_request_routed() {
  local token="$1"
  local expected_app="$2" # e.g., testtarget2
  echo "Verifying routing for token=${token} expected_app=${expected_app} ..."

  local attempts=10
  local sleep_sec=2
  local ok=1

  for ((a=1; a<=attempts; a++)); do
    local found_expected=0
    local found_unexpected=0

    for app in testtarget1 testtarget2 testtarget3; do
      # Get pod(s) for this app
      for pod in $(kubectl get pods -l app=${app} -o name 2>/dev/null); do
        if kubectl logs "$pod" --tail=500 2>/dev/null | grep -q "$token"; then
          if [ "$app" = "$expected_app" ]; then
            found_expected=1
          else
            found_unexpected=1
          fi
        fi
      done
    done

    if [ $found_expected -eq 1 ] && [ $found_unexpected -eq 0 ]; then
      ok=0
      echo "Routing verification passed: token seen only in ${expected_app} logs."
      break
    fi

    echo "Waiting for logs to flush (attempt $a/$attempts)..."
    sleep "$sleep_sec"
  done

  if [ $ok -ne 0 ]; then
    echo "Routing verification FAILED for token=${token}. Expected only ${expected_app} to receive the request."
    echo "-- Recent logs (tail) for target pods --"
    for sel in "app=testtarget1" "app=testtarget2" "app=testtarget3"; do
      kubectl get pods -l "$sel" -o name 2>/dev/null | while IFS= read -r p; do
        echo "--- logs for $p (tail 200) ---"
        kubectl logs "$p" --tail=200 2>/dev/null || true
      done
    done
    exit 1
  fi
}

# Combined test: send request and verify it reached the correct backend
function test_host_routes_to_service() {
  local host="$1"
  local expected_app="$2"
  echo "Testing Host: ${host} should route to app=${expected_app} ..."
  local token
  token="$(_gen_token)"
  curl_with_host_and_token "$host" "$token"
  assert_request_routed "$token" "$expected_app"
}

# Execute tests for each instance
test_host_routes_to_service app1.local testtarget1
test_host_routes_to_service app2.local testtarget2
test_host_routes_to_service app3.local testtarget3

echo "=== Multi-Instance Host Header Test Completed Successfully ==="

exit 0
