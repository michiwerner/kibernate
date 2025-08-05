#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget
  kubectl delete service testtarget
  kubectl delete service kibernate-test 2>/dev/null || true
}
trap finally EXIT

kubectl create deployment testtarget --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080
./scripts/install-helm-chart.sh -f ./configs/tests/helm/01-test-http-passthrough.yml
kubectl wait --for=condition=available --timeout=60s deployment/testtarget
kubectl wait --for=condition=available --timeout=60s deployment/kibernate

# Create a shorter-named service for easier DNS resolution
kubectl expose deployment kibernate --name=kibernate-test --port=8080 --target-port=8080 || true

# Wait for the kibernate service to be created and endpoints to be ready
until kubectl get service kibernate-test &> /dev/null; do
  echo "Waiting for kibernate-test service to be created..."
  sleep 2
done

# Wait for endpoints to be ready with better checking
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

# Test with retry logic
kubectl run -i --rm test --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "
set -eo pipefail
sleep 5
i=1
while [ \$i -le 5 ]; do
  echo \"Attempt \$i/5 to connect to kibernate-test:8080\"
  if curl -f --connect-timeout 10 --max-time 30 'http://kibernate-test:8080' 2>/dev/null | tee > /tmp/curl_out.txt; then
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