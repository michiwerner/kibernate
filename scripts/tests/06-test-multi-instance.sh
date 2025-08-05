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

# Wait for all services to be ready with endpoints
echo "Waiting for service endpoints to be ready..."
for service in kibernate-instance1 kibernate-instance2 kibernate-instance3; do
  echo "Waiting for $service endpoints..."
  for i in {1..30}; do
    if kubectl get endpoints $service &> /dev/null && \
       [ "$(kubectl get endpoints $service -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null)" ]; then
      echo "$service endpoints are ready"
      break
    fi
    echo "Waiting for $service endpoints... (attempt $i/30)"
    sleep 2
  done
done

# Additional wait for DNS propagation
sleep 5

# Test instance 1 (port 8080)
echo "Testing instance 1 on port 8080..."
kubectl run -i --rm test1 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
set -eo pipefail
sleep 5
for i in {1..5}; do
  echo \"Attempt \$i/5 to connect to kibernate-instance1:8080\"
  if curl -f --connect-timeout 10 --max-time 30 'http://kibernate-instance1:8080' 2>/dev/null | tee > /tmp/curl_out.txt; then
    echo
    if grep -q 'Thank you for using nginx.' /tmp/curl_out.txt; then
      echo \"Instance 1 test successful!\"
      exit 0
    else
      echo \"Response received but content doesn't match expected pattern\"
    fi
  else
    echo \"Attempt \$i failed, waiting before retry...\"
    sleep 5
  fi
done
echo \"All attempts for instance 1 failed\"
exit 1
"

# Test instance 2 (port 8081)
echo "Testing instance 2 on port 8081..."
kubectl run -i --rm test2 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
set -eo pipefail
sleep 5
for i in {1..5}; do
  echo \"Attempt \$i/5 to connect to kibernate-instance2:8081\"
  if curl -f --connect-timeout 10 --max-time 30 'http://kibernate-instance2:8081' 2>/dev/null | tee > /tmp/curl_out.txt; then
    echo
    if grep -q 'Thank you for using nginx.' /tmp/curl_out.txt; then
      echo \"Instance 2 test successful!\"
      exit 0
    else
      echo \"Response received but content doesn't match expected pattern\"
    fi
  else
    echo \"Attempt \$i failed, waiting before retry...\"
    sleep 5
  fi
done
echo \"All attempts for instance 2 failed\"
exit 1
"

# Test instance 3 (port 8082)
echo "Testing instance 3 on port 8082..."
kubectl run -i --rm test3 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
set -eo pipefail
sleep 5
for i in {1..5}; do
  echo \"Attempt \$i/5 to connect to kibernate-instance3:8082\"
  if curl -f --connect-timeout 10 --max-time 30 'http://kibernate-instance3:8082' 2>/dev/null | tee > /tmp/curl_out.txt; then
    echo
    if grep -q 'Thank you for using nginx.' /tmp/curl_out.txt; then
      echo \"Instance 3 test successful!\"
      exit 0
    else
      echo \"Response received but content doesn't match expected pattern\"
    fi
  else
    echo \"Attempt \$i failed, waiting before retry...\"
    sleep 5
  fi
done
echo \"All attempts for instance 3 failed\"
exit 1
"

echo "=== Multi-Instance Test Completed Successfully ==="