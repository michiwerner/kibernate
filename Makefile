deploy-testtarget:
	./scripts/deploy-testtarget.sh

install-helm-chart:
	./scripts/install-helm-chart.sh

prepare-testing-env:
	./scripts/prepare-testing-env.sh

tear-down-testing-env:
	./scripts/tear-down-testing-env.sh

test-all: prepare-testing-env docker-build \
	01-test-http-passthrough \
	02-test-http-activation \
	03-test-companion-deployment-activation \
	04-test-http-deactivation \
	05-test-companion-deployment-deactivation \
	tear-down-testing-env

docker-build:
	./scripts/docker-build.sh

helm-lint:
	helm lint --strict ./deployments/helm/kibernate-chart

helm-package:
	helm package ./deployments/helm/kibernate-chart

helm-publish:
	helm push ./deployments/helm/kibernate-chart-*.tgz oci://ghcr.io/michiwerner

docker-buildx-push-ghcr:
ifndef IMAGE_TAGS
	$(error IMAGE_TAGS is undefined)
endif
	./scripts/docker-buildx-push-ghcr.sh $(IMAGE_TAGS)

01-test-http-passthrough:
	./scripts/tests/01-test-http-passthrough.sh
	
02-test-http-activation:
	./scripts/tests/02-test-http-activation.sh
	
03-test-companion-deployment-activation:
	./scripts/tests/03-test-companion-deployment-activation.sh
	
04-test-http-deactivation:
	./scripts/tests/04-test-http-deactivation.sh
	
05-test-companion-deployment-deactivation:
	./scripts/tests/05-test-companion-deployment-deactivation.sh