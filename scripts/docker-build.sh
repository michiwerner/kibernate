#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/..

docker build -f Dockerfile -t kibernate:latest .

if [[ -n "$(command -v minikube)" ]] && minikube profile list | grep kibernate-test; then
  echo "minikube profile kibernate-test exists - importing image into minikube"
  docker save kibernate:latest | (eval "$(minikube docker-env -p kibernate-test)" && docker load)
fi

