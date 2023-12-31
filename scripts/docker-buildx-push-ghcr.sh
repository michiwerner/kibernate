#!/usr/bin/env bash

set -eo pipefail

if [[ -z "$1" ]]; then
  echo "Usage: $0 <tags>"
  exit 1
fi

cd "$(dirname "$0")"/..

for tag in "$@"; do
  tags="$tags -t ghcr.io/michiwerner/kibernate:$tag"
done

#docker buildx build --platform linux/amd64 $tags --push -f Dockerfile .

docker build $tags -f package/docker/Dockerfile .

for tag in "$@"; do
  docker push "ghcr.io/michiwerner/kibernate:$tag"
done