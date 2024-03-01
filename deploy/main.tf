terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">=3.78.0"
    }
  }
}

provider "azurerm" {
  features {}
}

locals {
  tags = {
    env  = var.env
    name = "webm-to-mp4-tg-bot"
  }
}

resource "azurerm_resource_group" "rg-webm-to-mp4-tg-bot" {
  name     = "rg-webm-to-mp4-tg-bot-${var.env}"
  location = "France Central"

  tags = local.tags
}

resource "azurerm_application_insights" "appi-webm-to-mp4-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-webm-to-mp4-tg-bot.name
  location            = azurerm_resource_group.rg-webm-to-mp4-tg-bot.location

  name             = "appi-webm-to-mp4-tg-bot-${var.env}"
  application_type = "web"
}

resource "azurerm_storage_account" "st-webm-to-mp4-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-webm-to-mp4-tg-bot.name
  location            = azurerm_resource_group.rg-webm-to-mp4-tg-bot.location

  name                     = "stwebmtomp4tgbot${var.env}"
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "Storage"

  tags = local.tags
}

resource "azurerm_storage_queue" "stq-downloader-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "downloader"
}

resource "azurerm_storage_queue" "stq-converter-input-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "converter-input"
}

resource "azurerm_storage_queue" "stq-converter-output-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "converter-output"
}

resource "azurerm_storage_queue" "stq-thumbnailer-input-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "thumbnailer-input"
}

resource "azurerm_storage_queue" "stq-thumbnailer-output-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "thumbnailer-output"
}

resource "azurerm_storage_container" "stc-converter-input-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "converter-input"
}

resource "azurerm_storage_container" "stc-converter-output-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "converter-output"
}

resource "azurerm_storage_container" "stc-thumbnailer-input-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "thumbnailer-input"
}

resource "azurerm_storage_container" "stc-thumbnailer-output-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "thumbnailer-output"
}

resource "azurerm_storage_queue" "stq-uploader-webm-to-mp4-tg-bot" {
  storage_account_name = azurerm_storage_account.st-webm-to-mp4-tg-bot.name

  name = "uploader"
}

resource "azurerm_service_plan" "asp-webm-to-mp4-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-webm-to-mp4-tg-bot.name
  location            = azurerm_resource_group.rg-webm-to-mp4-tg-bot.location

  name     = "asp-webm-to-mp4-tg-bot-${var.env}"
  os_type  = "Linux"
  sku_name = "Y1"

  tags = local.tags
}

resource "azurerm_linux_function_app" "func-webm-to-mp4-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-webm-to-mp4-tg-bot.name
  location            = azurerm_resource_group.rg-webm-to-mp4-tg-bot.location

  storage_account_name       = azurerm_storage_account.st-webm-to-mp4-tg-bot.name
  storage_account_access_key = azurerm_storage_account.st-webm-to-mp4-tg-bot.primary_access_key
  service_plan_id            = azurerm_service_plan.asp-webm-to-mp4-tg-bot.id

  name = "func-webm-to-mp4-tg-bot-${var.env}"

  functions_extension_version = "~4"
  builtin_logging_enabled     = false

  site_config {
    application_insights_key = azurerm_application_insights.appi-webm-to-mp4-tg-bot.instrumentation_key
    app_scale_limit          = 10

    application_stack {
      dotnet_version              = "8.0"
      use_dotnet_isolated_runtime = true
    }
  }

  app_settings = {
    Telegram__Token  = var.telegram-token
    Telegram__ApiUrl = var.telegram-api-url

    Database__ConnectionString = var.database-connection-string
    Database__Name             = var.database-name

    Workers__ConnectionString  = azurerm_storage_account.st-webm-to-mp4-tg-bot.primary_connection_string
    Workers__Downloader__Queue = azurerm_storage_queue.stq-downloader-webm-to-mp4-tg-bot.name

    Workers__Converter__Input__Container = azurerm_storage_container.stc-converter-input-webm-to-mp4-tg-bot.name
    Workers__Converter__Input__Queue     = azurerm_storage_queue.stq-converter-input-webm-to-mp4-tg-bot.name

    Workers__Converter__Output__Container = azurerm_storage_container.stc-converter-output-webm-to-mp4-tg-bot.name
    Workers__Converter__Output__Queue     = azurerm_storage_queue.stq-converter-output-webm-to-mp4-tg-bot.name

    Workers__Thumbnailer__Input__Container = azurerm_storage_container.stc-thumbnailer-input-webm-to-mp4-tg-bot.name
    Workers__Thumbnailer__Input__Queue     = azurerm_storage_queue.stq-thumbnailer-input-webm-to-mp4-tg-bot.name

    Workers__Thumbnailer__Output__Container = azurerm_storage_container.stc-thumbnailer-output-webm-to-mp4-tg-bot.name
    Workers__Thumbnailer__Output__Queue     = azurerm_storage_queue.stq-thumbnailer-output-webm-to-mp4-tg-bot.name

    Workers__Uploader__Queue = azurerm_storage_queue.stq-uploader-webm-to-mp4-tg-bot.name
  }

  tags = local.tags
}
