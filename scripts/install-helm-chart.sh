#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/..

helm upgrade --install \
		-n default \
		kibernate \
		./deployments/helm/kibernate \
		--set image.repository=kibernate \
		--set image.tag=latest \
		--set image.pullPolicy=Never \
		"$@"
