#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/..

kubectl create deployment testtarget --image=ghcr.io/nginxinc/nginx-unprivileged:1.23-alpine --replicas=1 --port=8080
kubectl expose deployment testtarget --port=8080 --target-port=8080
