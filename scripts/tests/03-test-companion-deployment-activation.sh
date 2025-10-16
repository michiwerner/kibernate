#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  exit_code=${1:-0}
  set +eo pipefail
  kubectl delete pod test > /dev/null 2>&1 || true # this will fail under normal circumstances, thus all outputs are hidden
  kubectl delete deployment testtarget 2>/dev/null || true
  kubectl delete deployment testtarget-companion1 2>/dev/null || true
  kubectl delete deployment testtarget-companion2 2>/dev/null || true
  kubectl delete service testtarget 2>/dev/null || true
  kubectl delete service testtarget-companion1 2>/dev/null || true
  kubectl delete service testtarget-companion2 2>/dev/null || true
  exit "$exit_code"
}
trap 'finally $?' EXIT

kubectl create deployment testtarget --image=nginxinc/nginx-unprivileged:latest --replicas=0 --port=8080
kubectl create deployment testtarget-companion1 --image=nginxinc/nginx-unprivileged:latest --replicas=0 --port=8080
kubectl create deployment testtarget-companion2 --image=nginxinc/nginx-unprivileged:latest --replicas=0 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080
kubectl expose deployment testtarget-companion1 --port=8080 --target-port=8080
kubectl expose deployment testtarget-companion2 --port=8080 --target-port=8080
./scripts/install-helm-chart.sh -f ./configs/tests/helm/03-test-companion-deployment-activation.yml
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
while true; do
  currentReplicas=$(kubectl get deployments.apps testtarget-companion1 -o=jsonpath='{.status.availableReplicas}')
  if [ "$currentReplicas" = "1" ]; then
    break
  fi
  if [ $(($(date +%s) - start)) -gt 60 ]; then
    echo "Timeout waiting for testtarget-companion1 to scale up"
    exit 1
  fi
  sleep 1
done
while true; do
  currentReplicas=$(kubectl get deployments.apps testtarget-companion2 -o=jsonpath='{.status.availableReplicas}')
  if [ "$currentReplicas" = "1" ]; then
    break
  fi
  if [ $(($(date +%s) - start)) -gt 60 ]; then
    echo "Timeout waiting for testtarget-companion2 to scale up"
    exit 1
  fi
  sleep 1
done
