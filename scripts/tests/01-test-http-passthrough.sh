#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget
  kubectl delete service testtarget
}
trap finally EXIT

kubectl create deployment testtarget --image=ghcr.io/nginxinc/nginx-unprivileged:1.23-alpine --replicas=1 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080
./scripts/install-helm-chart.sh -f ./configs/tests/helm/01-test-http-passthrough.yml
kubectl wait --for=condition=available --timeout=60s deployment/testtarget
kubectl wait --for=condition=available --timeout=60s deployment/kibernate

kubectl run -i --rm test --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "set -eo pipefail; sleep 10; curl 'http://kibernate:8080' | tee > /tmp/curl_out.txt; echo; cat /tmp/curl_out.txt | grep 'Thank you for using nginx.'"