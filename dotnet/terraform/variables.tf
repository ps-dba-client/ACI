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
  description = "Container image tag pushed to ACR (must exist before the full terraform apply)."
  default     = "v1"
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
