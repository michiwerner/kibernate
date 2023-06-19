#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/..

if [ ! -d "kibernate-helm" ]; then
  cleanup=1
  ./scripts/prepare-helm-chart.sh
fi

helm upgrade --install \
		-n default \
		kibernate \
		./kibernate-helm/kibernate \
		--set image.repository=kibernate \
		--set image.tag=latest \
		--set image.pullPolicy=Never \
		"$@"
		
if [ -n "$cleanup" ]; then
  echo "Cleaning up kibernate-helm directory"
  rm -rf kibernate-helm
fi