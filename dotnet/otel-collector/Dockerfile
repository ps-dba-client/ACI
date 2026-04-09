# Splunk OTel Collector sidecar with lab OTLP → HEC config (no secrets in image).
# Build from repo root (dotnet/): az acr build -f otel-collector/Dockerfile -t aca-otel-collector:v1 .
FROM quay.io/signalfx/splunk-otel-collector:0.108.1
COPY otel-collector/collector.yaml /etc/splunk-otel/collector.yaml
