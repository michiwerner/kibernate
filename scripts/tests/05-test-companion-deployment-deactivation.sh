#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  exit_code=${1:-0}
  set +eo pipefail

  if [ "$exit_code" != "0" ]; then
    echo "==== DEBUG: Collecting pod logs because the script failed (exit code $exit_code) ===="
    echo "-- kibernate pods --"
    kubectl get pods -l app.kubernetes.io/name=kibernate -o wide || true
    kubectl describe deployment kibernate || true
    for p in $(kubectl get pods -l app.kubernetes.io/name=kibernate -o name 2>/dev/null); do
      echo "--- logs for $p ---"
      kubectl logs "$p" --tail=200 || true
    done

    echo "-- target pods --"
    for sel in "app=testtarget" "app=testtarget-companion1" "app=testtarget-companion2"; do
      kubectl get pods -l "$sel" -o wide || true
      for p in $(kubectl get pods -l "$sel" -o name 2>/dev/null); do
        echo "--- logs for $p ---"
        kubectl logs "$p" --tail=200 || true
      done
    done
    echo "==== END DEBUG LOGS ===="
  fi

  kubectl delete deployment testtarget 2>/dev/null || true
  kubectl delete deployment testtarget-companion1 2>/dev/null || true
  kubectl delete deployment testtarget-companion2 2>/dev/null || true
  kubectl delete service testtarget 2>/dev/null || true
  kubectl delete service testtarget-companion1 2>/dev/null || true
  kubectl delete service testtarget-companion2 2>/dev/null || true
}
trap 'finally $?' EXIT

kubectl create deployment testtarget --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl create deployment testtarget-companion1 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl create deployment testtarget-companion2 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080
kubectl expose deployment testtarget-companion1 --port=8080 --target-port=8080
kubectl expose deployment testtarget-companion2 --port=8080 --target-port=8080
./scripts/install-helm-chart.sh -f ./configs/tests/helm/05-test-companion-deployment-deactivation.yml
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
while true; do
  currentReplicas=$(kubectl get deployments.apps testtarget-companion1 -o=jsonpath='{.status.availableReplicas}')
  if [ "$currentReplicas" != "1" ]; then
    break
  fi
  if [ $(($(date +%s) - start)) -gt 60 ]; then
    echo "Timeout waiting for testtarget-companion1 to scale down"
    exit 1
  fi
  sleep 1
done
while true; do
  currentReplicas=$(kubectl get deployments.apps testtarget-companion2 -o=jsonpath='{.status.availableReplicas}')
  if [ "$currentReplicas" != "1" ]; then
    break
  fi
  if [ $(($(date +%s) - start)) -gt 60 ]; then
    echo "Timeout waiting for testtarget-companion2 to scale down"
    exit 1
  fi
  sleep 1
done
