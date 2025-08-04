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
./scripts/install-helm-chart.sh -f ./configs/tests/helm/04-test-http-deactivation.yml
kubectl wait --for=condition=available --timeout=60s deployment/kibernate
start=$(date +%s)
while true; do
  currentReplicas=$(kubectl get deployments.apps testtarget -o=jsonpath='{.status.availableReplicas}')
  if [ "$currentReplicas" != "1" ]; then
    break
  fi
  if [ $(($(date +%s) - start)) -gt 60 ]; then
    echo "Timeout waiting for testtarget to scale down"
    exit 1
  fi
  sleep 1
done
