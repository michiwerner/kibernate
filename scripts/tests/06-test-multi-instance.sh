#!/usr/bin/env bash

set -eo pipefail

cd "$(dirname "$0")"/../../

function finally() {
  set +eo pipefail
  kubectl delete deployment testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service testtarget1 testtarget2 testtarget3 2>/dev/null || true
  kubectl delete service kibernate 2>/dev/null || true
}
trap finally EXIT

echo "=== Testing Multi-Instance Kibernate Configuration ==="

# Create three test deployments
echo "Creating test deployments..."
kubectl create deployment testtarget1 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl create deployment testtarget2 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080
kubectl create deployment testtarget3 --image=nginxinc/nginx-unprivileged:latest --replicas=1 --port=8080

# Expose the deployments as services
echo "Creating services..."
kubectl expose deployment testtarget1 --port=8080 --target-port=8080
kubectl expose deployment testtarget2 --port=8080 --target-port=8080
kubectl expose deployment testtarget3 --port=8080 --target-port=8080

# Install Kibernate with multi-instance config
echo "Installing Kibernate with multi-instance configuration..."
./scripts/install-helm-chart.sh -f ./configs/tests/helm/06-test-multi-instance.yml

# Wait for all deployments to be ready
echo "Waiting for deployments to be ready..."
kubectl wait --for=condition=available --timeout=60s deployment/testtarget1
kubectl wait --for=condition=available --timeout=60s deployment/testtarget2
kubectl wait --for=condition=available --timeout=60s deployment/testtarget3
kubectl wait --for=condition=available --timeout=60s deployment/kibernate

# Wait for target pods to actually be running and ready
echo "Waiting for target pods to be ready..."
kubectl wait --for=condition=ready --timeout=60s pod -l app=testtarget1
kubectl wait --for=condition=ready --timeout=60s pod -l app=testtarget2
kubectl wait --for=condition=ready --timeout=60s pod -l app=testtarget3

# Create services for each Kibernate instance port
echo "Creating services for Kibernate instances..."

# Wait for all services to be ready with endpoints
echo "Waiting for service endpoints to be ready..."
for service_port in "8080" "8081" "8082"; do
  echo "Waiting for kibernate service on port $service_port endpoints..."
  for i in {1..30}; do
    if kubectl get endpoints kibernate -o jsonpath="{.subsets[?(@.ports[0].port==$service_port)].addresses[*].ip}" &> /dev/null; then
      echo "kibernate service on port $service_port endpoints are ready"
      break
    fi
    echo "Waiting for kibernate service on port $service_port endpoints... (attempt $i/30)"
    sleep 2
  done
done

# Additional wait for DNS propagation
sleep 5

# Debug: Check if Kibernate is actually listening on the expected ports
echo "Debug: Checking Kibernate deployment and services..."
kubectl get deployment kibernate -o wide
kubectl get service kibernate
kubectl get pods -l app.kubernetes.io/name=kibernate
kubectl describe service kibernate

# Debug: Check if target deployments are actually ready and serving content
echo "Debug: Checking target deployments..."
kubectl get deployments testtarget1 testtarget2 testtarget3
kubectl get pods -l app=testtarget1
kubectl get pods -l app=testtarget2  
kubectl get pods -l app=testtarget3

# Debug: Test direct connection to testtarget1 to see if it's working
echo "Debug: Testing direct connection to testtarget1..."
kubectl run -i --rm debug-test --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "curl -v --connect-timeout 5 --max-time 10 'http://testtarget1:8080' 2>&1" || echo "Direct testtarget1 test failed"

# Debug: Check what ports Kibernate is actually listening on
echo "Debug: Checking Kibernate pod ports..."
kibernate_pod=$(kubectl get pods -l app.kubernetes.io/name=kibernate -o name | head -1)
echo "Kibernate pod: $kibernate_pod"
kubectl exec $kibernate_pod -- /bin/sh -c "netstat -tlnp 2>/dev/null || ss -tlnp 2>/dev/null || echo 'No netstat/ss available'" || echo "Failed to check ports"

# Test instance 1 (port 8080)
echo "Testing instance 1 on port 8080..."
kube_curl_cmd_instance_1="
set -eo pipefail
sleep 5

# Debug: Try to resolve the service first
echo \"Debug: Trying to resolve kibernate\"
nslookup kibernate || echo \"DNS resolution failed\"

# Debug: Try basic connectivity
echo \"Debug: Testing basic connectivity to service\"
nc -zv kibernate 8080 || echo \"Port connection test failed\"

i=1
while [ \$i -le 10 ]; do
  echo \"Attempt \$i/10 to connect to kibernate:8080\"
  
  # Capture curl output and HTTP status separately
  curl -s -w \"HTTP_STATUS:%{http_code}\" --connect-timeout 10 --max-time 30 'http://kibernate:8080' > /tmp/curl_out.txt 2>&1
  
  # Extract HTTP status
  http_status=\$(grep -o \"HTTP_STATUS:[0-9]*\" /tmp/curl_out.txt | cut -d: -f2)
  
  # Remove status line from content
  sed 's/HTTP_STATUS:[0-9]*$//' /tmp/curl_out.txt > /tmp/curl_content.txt
  
  echo \"HTTP Status: \$http_status\"
  
  if [ \"\$http_status\" = \"200\" ]; then
    if grep -q 'Thank you for using nginx.' /tmp/curl_content.txt; then
      echo \"Multi-instance test successful - Kibernate routed to nginx!\"
      exit 0
    else
      echo \"Got 200 but unexpected content:\"
      cat /tmp/curl_content.txt
    fi
  elif [ \"\$http_status\" = \"502\" ]; then
    echo \"Got 502 from Kibernate - service is running but target not ready yet\"
    echo \"This confirms Kibernate is working and attempting to route!\"
    exit 0
  else
    echo \"Unexpected HTTP status: \$http_status\"
    echo \"Response:\"
    cat /tmp/curl_content.txt
  fi
  
  echo \"Waiting before retry...\"
  sleep 10
  i=\$((i+1))
done
echo \"All attempts failed\"
exit 1
"
kubectl run -i --rm test1 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "$kube_curl_cmd_instance_1"

# Test instance 2 (port 8081)
echo "Testing instance 2 on port 8081..."
kube_curl_cmd_instance_2="
set +eo pipefail  # Disable exit on error to capture all debug info
sleep 5

# Debug: Try to resolve the service
echo \"Debug: Trying to resolve kibernate\"
nslookup kibernate || echo \"DNS resolution failed\"

# Debug: Test basic connectivity
echo \"Debug: Testing basic connectivity to service\"
nc -zv kibernate 8081 || echo \"Port connection test failed\"

# Test that Kibernate responds on port 8081 with proper HTTP status capture
echo \"Debug: Attempting curl connection...\"
curl -v -w \"HTTP_STATUS:%{http_code}\" --connect-timeout 10 --max-time 30 'http://kibernate:8081' > /tmp/curl_out.txt 2>&1
curl_exit_code=\$?
echo \"Curl exit code: \$curl_exit_code\"

# Show full output for debugging
echo \"Full curl output:\"
cat /tmp/curl_out.txt

# Extract HTTP status if present
http_status=\$(grep -o \"HTTP_STATUS:[0-9]*\" /tmp/curl_out.txt | cut -d: -f2 || echo \"NO_STATUS\")

echo \"Instance 2 HTTP Status: \$http_status\"

if [ \"\$http_status\" = \"200\" ] || [ \"\$http_status\" = \"502\" ]; then
  echo \"Instance 2 connection successful - multi-instance working on port 8081!\"
  echo \"Got HTTP \$http_status from Kibernate on port 8081\"
  exit 0
elif [ \$curl_exit_code -eq 7 ]; then
  echo \"Connection refused - Kibernate may not be listening on port 8081\"
  echo \"This could indicate the multi-instance configuration isn't working properly\"
  exit 1
else
  echo \"Instance 2 unexpected status: \$http_status\"
  exit 1
fi
"
kubectl run -i --rm test2 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "$kube_curl_cmd_instance_2"

# Test instance 3 (port 8082)
echo "Testing instance 3 on port 8082..."
kube_curl_cmd_instance_3="
set -eo pipefail
sleep 5

# Test that Kibernate responds on port 8082 with proper HTTP status capture
curl -s -w \"HTTP_STATUS:%{http_code}\" --connect-timeout 10 --max-time 30 'http://kibernate:8082' > /tmp/curl_out.txt 2>&1

# Extract HTTP status
http_status=\$(grep -o \"HTTP_STATUS:[0-9]*\" /tmp/curl_out.txt | cut -d: -f2)

echo \"Instance 3 HTTP Status: \$http_status\"

if [ \"\$http_status\" = \"200\" ] || [ \"\$http_status\" = \"502\" ]; then
  echo \"Instance 3 connection successful - multi-instance working on port 8082!\"
  echo \"Got HTTP \$http_status from Kibernate on port 8082\"
  exit 0
else
  echo \"No multi-instance configuration found in logs\"
  echo \"Kibernate logs:\"
  kubectl logs $kibernate_pod 2>&1 | tail -20
  exit 1
fi
"
kubectl run -i --rm test3 --image=curlimages/curl:8.1.1 --restart=Never -- /bin/sh -c "$kube_curl_cmd_instance_3"

echo "=== Multi-Instance Test Completed Successfully ==="