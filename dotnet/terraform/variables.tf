variable "location" {
  type        = string
  description = "Azure region (pick one close to you; affects latency only for this lab)."
  default     = "eastus2"
}

variable "prefix" {
  type        = string
  description = "Short prefix used in resource names."
  default     = "acadotlab"
}

variable "image_tag" {
  type        = string
  description = "Container image tag for the .NET app (and default tag for the collector unless collector_image_tag is set)."
  default     = "v1"
}

variable "collector_image_tag" {
  type        = string
  nullable    = true
  default     = null
  description = "Optional: tag only for aca-otel-collector (e.g. v2 after changing collector.yaml). Defaults to image_tag."
}

variable "min_replicas" {
  type        = number
  description = "Minimum Container App replicas. Set 0 to allow scale-to-zero (lowest cost; cold starts)."
  default     = 0
}

variable "max_replicas" {
  type    = number
  default = 2
}

variable "otel_service_name" {
  type        = string
  description = "Splunk / OpenTelemetry service.name resource attribute."
  default     = "aca-dotnet-otel-lab"
}

variable "deployment_environment" {
  type        = string
  description = "Splunk deployment.environment (resource attribute)."
  default     = "lab"
}

variable "splunk_hec_url" {
  type        = string
  description = "Splunk HEC URL, for example https://http-inputs-xxxx.splunkcloud.com/services/collector/event"
}

variable "splunk_index" {
  type        = string
  description = "Splunk index name for OTLP data exported via HEC."
}

variable "splunk_source" {
  type        = string
  description = "Splunk source field for exported events."
  default     = "aci"
}

variable "splunk_hec_token" {
  type        = string
  description = "Splunk HEC token (set via environment as TF_VAR_splunk_hec_token or a private .tfvars file — never commit)."
  sensitive   = true
}

variable "splunk_observability_realm" {
  type        = string
  description = "Splunk Observability Cloud realm (e.g. us1, us0, eu0). Builds api.<realm>.signalfx.com and ingest.<realm>.signalfx.com."
  default     = "us1"
}

variable "splunk_observability_access_token" {
  type        = string
  description = "Splunk Observability Cloud access token (traces + metrics). TF_VAR_splunk_observability_access_token or private .tfvars — never commit."
  sensitive   = true
}
