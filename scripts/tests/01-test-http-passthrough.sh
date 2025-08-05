#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget
  kubectl delete service testtarget
}
trap finally EXIT

kubectl create deployment testtarget --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080
./scripts/install-helm-chart.sh -f ./configs/tests/helm/01-test-http-passthrough.yml
kubectl wait --for=condition=available --timeout=60s deployment/testtarget
kubectl wait --for=condition=available --timeout=60s deployment/kibernate

# Wait for the kibernate service to be created and endpoints to be ready
until kubectl get service kibernate &> /dev/null; do
  echo "Waiting for kibernate service to be created..."
  sleep 2
done
until kubectl get endpoints kibernate &> /dev/null && [ "$(kubectl get endpoints kibernate -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null)" ]; do
  echo "Waiting for kibernate service endpoints to be ready..."
  sleep 2
done
sleep 5  # Additional wait for DNS propagation

kubectl run -i --rm test --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "set -eo pipefail; sleep 10; curl 'http://kibernate.default.svc.cluster.local:8080' | tee > /tmp/curl_out.txt; echo; cat /tmp/curl_out.txt | grep 'Thank you for using nginx.'"