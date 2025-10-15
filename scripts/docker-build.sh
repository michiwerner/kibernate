#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/..

docker build -f package/docker/Dockerfile -t kibernate:latest .

# If a minikube profile exists, load the image into the cluster so Helm can use imagePullPolicy=Never
if [[ -n "$(command -v minikube)" ]] && minikube profile list | grep -q kibernate-test; then
  echo "minikube profile kibernate-test exists - loading image into minikube"
  minikube image load -p kibernate-test kibernate:latest
fi

