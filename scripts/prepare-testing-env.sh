#!/usr/bin/env bash

set -eo pipefail

minikube start --driver=docker --kubernetes-version=v1.33.3 -p kibernate-test
minikube profile kibernate-test
