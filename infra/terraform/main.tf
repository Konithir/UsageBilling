data "azurerm_client_config" "current" {}

locals {
  prefix = lower("${var.project_name}-${var.environment}")
  tags = {
    application = var.project_name
    environment = var.environment
  }
  postgres_connection = "Host=${azurerm_postgresql_flexible_server.billing.fqdn};Port=5432;Database=${azurerm_postgresql_flexible_server_database.billing.name};Username=${azurerm_postgresql_flexible_server.billing.administrator_login};Password=${var.database_password};Ssl Mode=Require"
  redis_connection    = "${azurerm_redis_cache.billing.hostname}:6380,password=${azurerm_redis_cache.billing.primary_access_key},ssl=True,abortConnect=False"
}

resource "azurerm_resource_group" "billing" {
  name     = "rg-${local.prefix}"
  location = var.location
  tags     = local.tags
}

resource "azurerm_log_analytics_workspace" "billing" {
  name                = "log-${local.prefix}"
  location            = azurerm_resource_group.billing.location
  resource_group_name = azurerm_resource_group.billing.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

resource "azurerm_container_registry" "billing" {
  name                = replace("acr${local.prefix}", "-", "")
  resource_group_name = azurerm_resource_group.billing.name
  location            = azurerm_resource_group.billing.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.tags
}

resource "azurerm_user_assigned_identity" "apps" {
  name                = "id-${local.prefix}"
  location            = azurerm_resource_group.billing.location
  resource_group_name = azurerm_resource_group.billing.name
  tags                = local.tags
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.billing.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
}

resource "azurerm_postgresql_flexible_server" "billing" {
  name                          = "psql-${local.prefix}"
  resource_group_name           = azurerm_resource_group.billing.name
  location                      = azurerm_resource_group.billing.location
  version                       = "16"
  administrator_login           = "usagebilling"
  administrator_password        = var.database_password
  sku_name                      = "B_Standard_B1ms"
  storage_mb                    = 32768
  backup_retention_days         = 7
  public_network_access_enabled = true
  tags                          = local.tags
}

resource "azurerm_postgresql_flexible_server_database" "billing" {
  name      = "usage_billing"
  server_id = azurerm_postgresql_flexible_server.billing.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "azure" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.billing.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_redis_cache" "billing" {
  name                 = "redis-${local.prefix}"
  location             = azurerm_resource_group.billing.location
  resource_group_name  = azurerm_resource_group.billing.name
  capacity             = 0
  family               = "C"
  sku_name             = "Basic"
  non_ssl_port_enabled = false
  minimum_tls_version  = "1.2"
  tags                 = local.tags
}

resource "azurerm_eventhub_namespace" "billing" {
  name                = "evhns-${local.prefix}"
  location            = azurerm_resource_group.billing.location
  resource_group_name = azurerm_resource_group.billing.name
  sku                 = "Standard"
  capacity            = 1
  tags                = local.tags
}

resource "azurerm_eventhub" "usage" {
  name              = "usage-events"
  namespace_id      = azurerm_eventhub_namespace.billing.id
  partition_count   = 4
  message_retention = 3
}

resource "azurerm_eventhub_namespace_authorization_rule" "apps" {
  name                = "usage-billing-apps"
  namespace_name      = azurerm_eventhub_namespace.billing.name
  resource_group_name = azurerm_resource_group.billing.name
  listen              = true
  send                = true
  manage              = false
}

resource "azurerm_key_vault" "billing" {
  name                       = "kv-${local.prefix}"
  location                   = azurerm_resource_group.billing.location
  resource_group_name        = azurerm_resource_group.billing.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = false
  tags                       = local.tags
}

resource "azurerm_role_assignment" "key_vault_admin" {
  scope                = azurerm_key_vault.billing.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_key_vault_secret" "postgres" {
  name         = "postgres-connection"
  value        = local.postgres_connection
  key_vault_id = azurerm_key_vault.billing.id
  depends_on   = [azurerm_role_assignment.key_vault_admin]
}

resource "azurerm_container_app_environment" "billing" {
  name                       = "cae-${local.prefix}"
  location                   = azurerm_resource_group.billing.location
  resource_group_name        = azurerm_resource_group.billing.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.billing.id
  tags                       = local.tags
}

resource "azurerm_container_app" "api" {
  name                         = "ca-${local.prefix}-api"
  container_app_environment_id = azurerm_container_app_environment.billing.id
  resource_group_name          = azurerm_resource_group.billing.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.billing.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  secret {
    name  = "postgres"
    value = local.postgres_connection
  }
  secret {
    name  = "redis"
    value = local.redis_connection
  }
  secret {
    name  = "eventhub"
    value = azurerm_eventhub_namespace_authorization_rule.apps.primary_connection_string
  }
  secret {
    name  = "stripe"
    value = var.stripe_secret_key
  }

  template {
    min_replicas = 1
    max_replicas = 3

    container {
      name   = "api"
      image  = var.api_image
      cpu    = 0.5
      memory = "1Gi"

      env {

        name = "Storage__Provider"

        value = "Postgres"

      }
      env {
        name        = "Storage__ConnectionString"
        secret_name = "postgres"
      }
      env {
        name  = "Redis__Enabled"
        value = "true"
      }
      env {
        name        = "Redis__ConnectionString"
        secret_name = "redis"
      }
      env {
        name  = "Messaging__Provider"
        value = "EventHubs"
      }
      env {
        name  = "Messaging__BootstrapServers"
        value = "${azurerm_eventhub_namespace.billing.name}.servicebus.windows.net:9093"
      }
      env {
        name  = "Messaging__Topic"
        value = azurerm_eventhub.usage.name
      }
      env {
        name  = "Messaging__SaslUsername"
        value = "$ConnectionString"
      }
      env {
        name        = "Messaging__SaslPassword"
        secret_name = "eventhub"
      }
      env {
        name  = "Stripe__Mode"
        value = var.stripe_secret_key == "" ? "Demo" : "Test"
      }
      env {
        name        = "Stripe__SecretKey"
        secret_name = "stripe"
      }
    }
  }

  dapr {
    app_id       = "usage-billing-api"
    app_port     = 8080
    app_protocol = "http"
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  depends_on = [azurerm_role_assignment.acr_pull]
}

resource "azurerm_container_app" "worker" {
  name                         = "ca-${local.prefix}-worker"
  container_app_environment_id = azurerm_container_app_environment.billing.id
  resource_group_name          = azurerm_resource_group.billing.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.billing.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  secret {
    name  = "postgres"
    value = local.postgres_connection
  }
  secret {
    name  = "stripe"
    value = var.stripe_secret_key
  }

  template {
    min_replicas = 1
    max_replicas = 2

    container {
      name   = "billing-worker"
      image  = var.worker_image
      cpu    = 0.5
      memory = "1Gi"
      env {
        name  = "Storage__Provider"
        value = "Postgres"
      }
      env {
        name        = "Storage__ConnectionString"
        secret_name = "postgres"
      }
      env {
        name  = "Stripe__Mode"
        value = var.stripe_secret_key == "" ? "Demo" : "Test"
      }
      env {
        name        = "Stripe__SecretKey"
        secret_name = "stripe"
      }
    }
  }

  dapr {
    app_id       = "billing-worker"
    app_protocol = "grpc"
  }

  depends_on = [azurerm_role_assignment.acr_pull, azurerm_container_app.api]
}

resource "azurerm_container_app" "web" {
  name                         = "ca-${local.prefix}-web"
  container_app_environment_id = azurerm_container_app_environment.billing.id
  resource_group_name          = azurerm_resource_group.billing.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.apps.id]
  }

  registry {
    server   = azurerm_container_registry.billing.login_server
    identity = azurerm_user_assigned_identity.apps.id
  }

  template {
    min_replicas = 1
    max_replicas = 2
    container {
      name   = "web"
      image  = var.web_image
      cpu    = 0.25
      memory = "0.5Gi"
      env {
        name  = "API_UPSTREAM"
        value = "https://${azurerm_container_app.api.ingress[0].fqdn}"
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  depends_on = [azurerm_role_assignment.acr_pull]
}