#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/..
./scripts/prepare-testing-env.sh
function finally() {
  ./scripts/tear-down-testing-env.sh
}
trap finally EXIT
./scripts/prepare-helm-chart.sh
./scripts/docker-build.sh
./scripts/deploy-testtarget.sh
./scripts/install-helm-chart.sh
kubectl wait --for=condition=available --timeout=60s deployment/testtarget
kubectl wait --for=condition=available --timeout=60s deployment/kibernate
kubectl run -i --rm test --image=alpine:3 --restart=Never -- /bin/sh -c "set -eo pipefail; apk add curl; sleep 30; curl 'http://kibernate:8080' | tee curl_out.txt; echo; cat curl_out.txt | grep 'Thank you for using nginx.'"
