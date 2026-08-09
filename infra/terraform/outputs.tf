output "web_url" {
  value = "https://${azurerm_container_app.web.ingress[0].fqdn}"
}

output "api_internal_url" {
  value = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}

output "container_registry" {
  value = azurerm_container_registry.billing.login_server
}

output "event_hubs_kafka_endpoint" {
  value = "${azurerm_eventhub_namespace.billing.name}.servicebus.windows.net:9093"
}

output "key_vault_name" {
  value = azurerm_key_vault.billing.name
}