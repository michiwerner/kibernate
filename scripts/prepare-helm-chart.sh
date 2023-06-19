#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/..

BRANCH_NAME=$(git rev-parse --abbrev-ref HEAD)
rm -fr kibernate-helm
if [ "$BRANCH_NAME" != "main" ] && ! git clone -b $BRANCH_NAME --single-branch --depth 1 https://github.com/kibernate/kibernate-helm.git; then
  echo "Branch $BRANCH_NAME does not exist in kibernate-helm, falling back to main"
  git clone -b main --single-branch --depth 1 https://github.com/kibernate/kibernate-helm.git
else
  echo "Using branch $BRANCH_NAME in kibernate-helm"
  git clone -b $BRANCH_NAME --single-branch --depth 1 https://github.com/kibernate/kibernate-helm.git
fi
rm -fr kibernate-helm/.git
