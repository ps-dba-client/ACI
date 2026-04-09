resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

locals {
  base_name         = "${var.prefix}-${random_string.suffix.result}"
  collector_img_tag = var.collector_image_tag != null && var.collector_image_tag != "" ? var.collector_image_tag : var.image_tag
  app_image         = "${azurerm_container_registry.acr.login_server}/aca-otel-dotnet:${var.image_tag}"
  collector_image   = "${azurerm_container_registry.acr.login_server}/aca-otel-collector:${local.collector_img_tag}"
  splunk_ingest_url = "https://ingest.${var.splunk_observability_realm}.signalfx.com"
  splunk_api_url    = "https://api.${var.splunk_observability_realm}.signalfx.com"
}

resource "azurerm_resource_group" "main" {
  name     = "${local.base_name}-rg"
  location = var.location
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = "${local.base_name}-law"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_container_app_environment" "main" {
  name                       = "${local.base_name}-cae"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
}

resource "azurerm_container_registry" "acr" {
  name                = replace("${local.base_name}acr", "-", "")
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = true
}

resource "azurerm_container_app" "main" {
  name                         = "${local.base_name}-app"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type = "SystemAssigned"
  }

  secret {
    name  = "acr-password"
    value = azurerm_container_registry.acr.admin_password
  }

  secret {
    name  = "splunk-hec-token"
    value = var.splunk_hec_token
  }

  secret {
    name  = "splunk-o11y-access-token"
    value = var.splunk_observability_access_token
  }

  registry {
    server               = azurerm_container_registry.acr.login_server
    username             = azurerm_container_registry.acr.admin_username
    password_secret_name = "acr-password"
  }

  template {
    min_replicas = var.min_replicas
    max_replicas = var.max_replicas

    container {
      name   = "splunk-otel-collector"
      image  = local.collector_image
      cpu    = 0.25
      memory = "0.5Gi"

      args = ["--config=/etc/splunk-otel/collector.yaml"]

      env {
        name        = "SPLUNK_HEC_TOKEN"
        secret_name = "splunk-hec-token"
      }

      env {
        name        = "SPLUNK_ACCESS_TOKEN"
        secret_name = "splunk-o11y-access-token"
      }

      env {
        name  = "SPLUNK_INGEST_URL"
        value = local.splunk_ingest_url
      }

      env {
        name  = "SPLUNK_API_URL"
        value = local.splunk_api_url
      }

      env {
        name  = "SPLUNK_HEC_URL"
        value = var.splunk_hec_url
      }

      env {
        name  = "SPLUNK_INDEX"
        value = var.splunk_index
      }

      env {
        name  = "SPLUNK_SOURCE"
        value = var.splunk_source
      }

      env {
        name  = "DEPLOYMENT_ENVIRONMENT"
        value = var.deployment_environment
      }
    }

    container {
      name   = "dotnet-app"
      image  = local.app_image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "OTEL_SERVICE_NAME"
        value = var.otel_service_name
      }

      env {
        name  = "OTEL_RESOURCE_ATTRIBUTES"
        value = "deployment.environment=${var.deployment_environment}"
      }

      env {
        name  = "OTEL_EXPORTER_OTLP_ENDPOINT"
        value = "http://127.0.0.1:4317"
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  depends_on = [
    azurerm_container_app_environment.main,
    azurerm_container_registry.acr,
  ]
}
