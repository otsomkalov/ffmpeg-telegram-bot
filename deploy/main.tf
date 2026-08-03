terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">=4.64.0"
    }
  }
}

provider "azurerm" {
  features {}
}

locals {
  tags = {
    env  = var.env
    name = var.bot-name
  }
}

resource "azurerm_resource_group" "rg-tg-bot" {
  name     = "rg-${var.bot-name}-tg-bot-${var.env}"
  location = "France Central"

  tags = local.tags
}

resource "azurerm_log_analytics_workspace" "appi-ws-tg-bot" {
  name                = "appi-ws-${var.bot-name}-tg-bot-${var.env}"
  location            = azurerm_resource_group.rg-tg-bot.location
  resource_group_name = azurerm_resource_group.rg-tg-bot.name

  sku               = "PerGB2018"
  retention_in_days = 30

  tags = local.tags
}

resource "azurerm_application_insights" "appi-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location
  workspace_id        = azurerm_log_analytics_workspace.appi-ws-tg-bot.id

  name             = "appi-${var.bot-name}-tg-bot-${var.env}"
  application_type = "web"

  tags = local.tags
}

resource "azurerm_storage_account" "st-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location

  name                     = "st${var.bot-name}tgbot${var.env}"
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"

  tags = local.tags
}

resource "azurerm_storage_queue" "stq-downloader-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "downloader"
}

resource "azurerm_storage_queue" "stq-converter-input-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "converter-input"
}

resource "azurerm_storage_queue" "stq-converter-output-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "converter-output"
}

resource "azurerm_storage_queue" "stq-thumbnailer-input-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "thumbnailer-input"
}

resource "azurerm_storage_queue" "stq-thumbnailer-output-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "thumbnailer-output"
}

resource "azurerm_storage_container" "stc-converter-input-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "converter-input"
}

resource "azurerm_storage_container" "stc-converter-output-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "converter-output"
}

resource "azurerm_storage_container" "stc-thumbnailer-input-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "thumbnailer-input"
}

resource "azurerm_storage_container" "stc-thumbnailer-output-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "thumbnailer-output"
}

# Identity

resource "azurerm_user_assigned_identity" "ui-ado-pipeline" {
  location            = azurerm_resource_group.rg-tg-bot.location
  name                = "ui-ado-pipeline-${var.bot-name}-${var.env}"
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
}

resource "azurerm_storage_queue" "stq-uploader-tg-bot" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "uploader"
}

resource "azurerm_service_plan" "asp-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location

  name     = "asp-${var.bot-name}-tg-bot-${var.env}"
  os_type  = "Linux"
  sku_name = "FC1"

  tags = local.tags
}

resource "azurerm_storage_container" "stc-bot-deployments" {
  storage_account_id = azurerm_storage_account.st-tg-bot.id

  name = "deployments"
}

resource "azurerm_function_app_flex_consumption" "func-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location

  service_plan_id = azurerm_service_plan.asp-tg-bot.id

  name = "func-${var.bot-name}-tg-bot-${var.env}"

  runtime_name    = "dotnet-isolated"
  runtime_version = "10.0"

  storage_authentication_type = "StorageAccountConnectionString"
  storage_access_key          = azurerm_storage_account.st-tg-bot.primary_access_key
  storage_container_endpoint  = "${azurerm_storage_account.st-tg-bot.primary_blob_endpoint}${azurerm_storage_container.stc-bot-deployments.name}"
  storage_container_type      = "blobContainer"

  instance_memory_in_mb  = 512
  maximum_instance_count = 10

  identity {
    type = "SystemAssigned"
  }

  connection_string {
    name  = "APPLICATIONINSIGHTS"
    type  = "Custom"
    value = azurerm_application_insights.appi-tg-bot.connection_string
  }

  site_config {

  }

  app_settings = merge(
    {
      Telegram__Token  = var.telegram-token
      Telegram__ApiUrl = var.telegram-api-url

      Database__ConnectionString = var.database-connection-string
      Database__Name             = var.database-name

      Resources__DefaultLang = var.default-lang

      Workers__ConnectionString  = azurerm_storage_account.st-tg-bot.primary_connection_string
      Workers__Downloader__Queue = azurerm_storage_queue.stq-downloader-tg-bot.name

      Workers__Converter__Input__Container = azurerm_storage_container.stc-converter-input-tg-bot.name
      Workers__Converter__Input__Queue     = azurerm_storage_queue.stq-converter-input-tg-bot.name

      Workers__Converter__Output__Container = azurerm_storage_container.stc-converter-output-tg-bot.name
      Workers__Converter__Output__Queue     = azurerm_storage_queue.stq-converter-output-tg-bot.name

      Workers__Thumbnailer__Input__Container = azurerm_storage_container.stc-thumbnailer-input-tg-bot.name
      Workers__Thumbnailer__Input__Queue     = azurerm_storage_queue.stq-thumbnailer-input-tg-bot.name

      Workers__Thumbnailer__Output__Container = azurerm_storage_container.stc-thumbnailer-output-tg-bot.name
      Workers__Thumbnailer__Output__Queue     = azurerm_storage_queue.stq-thumbnailer-output-tg-bot.name

      Workers__Uploader__Queue = azurerm_storage_queue.stq-uploader-tg-bot.name

      Validation__LinkRegex = var.link-regex
    },
    {
      for idx, type in var.mime-types : "Validation__MimeTypes__${idx}" => type
  })

  tags = local.tags
}
