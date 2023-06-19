#!/usr/bin/env bash

set -eo pipefail

minikube start --driver=docker --kubernetes-version=v1.24.10 -p kibernate-test
minikube profile kibernate-test
