#!/usr/bin/env bash

set -eo pipefail

# Start minikube for testing; use default supported Kubernetes version for stability
minikube start --driver=docker -p kibernate-test
minikube profile kibernate-test
