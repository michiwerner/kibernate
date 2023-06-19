#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete pod test > /dev/null 2>&1 # this will fail under normal circumstances, thus all outputs are hidden
  kubectl delete deployment testtarget
  kubectl delete service testtarget
}
trap finally EXIT

kubectl create deployment testtarget --image=ghcr.io/nginxinc/nginx-unprivileged:1.23-alpine --replicas=0 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080
./scripts/install-helm-chart.sh -f ./configs/tests/helm/02-test-http-activation.yml
kubectl wait --for=condition=available --timeout=60s deployment/kibernate
kubectl run -i --rm test --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "sleep 10; curl 'http://kibernate:8080'; exit 0"
start=$(date +%s)
while true; do
  currentReplicas=$(kubectl get deployments.apps testtarget -o=jsonpath='{.status.availableReplicas}')
  if [ "$currentReplicas" = "1" ]; then
    break
  fi
  if [ $(($(date +%s) - start)) -gt 60 ]; then
    echo "Timeout waiting for testtarget to scale up"
    exit 1
  fi
  sleep 1
done
kubectl run -i --rm test --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "set -eo pipefail; sleep 15; curl 'http://kibernate:8080' | tee > /tmp/curl_out.txt; echo; cat /tmp/curl_out.txt | grep 'Thank you for using nginx.'"