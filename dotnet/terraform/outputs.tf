output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "acr_name" {
  value = azurerm_container_registry.acr.name
}

output "acr_login_server" {
  value = azurerm_container_registry.acr.login_server
}

output "container_app_name" {
  value = azurerm_container_app.main.name
}

output "container_app_fqdn" {
  value = azurerm_container_app.main.latest_revision_fqdn
}

output "public_url" {
  description = "Base URL for the sample API (HTTPS)."
  value       = "https://${azurerm_container_app.main.latest_revision_fqdn}"
}
