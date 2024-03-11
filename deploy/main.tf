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

resource "azurerm_resource_group" "rg-tg-bot" {
  name     = "rg-${var.bot-name}-tg-bot-${var.env}"
  location = "France Central"

  tags = local.tags
}

resource "azurerm_application_insights" "appi-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location

  name             = "appi-${var.bot-name}-tg-bot-${var.env}"
  application_type = "web"
}

resource "azurerm_storage_account" "st-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location

  name                     = "st${var.bot-name}tgbot${var.env}"
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "Storage"

  tags = local.tags
}

resource "azurerm_storage_queue" "stq-downloader-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "downloader"
}

resource "azurerm_storage_queue" "stq-converter-input-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "converter-input"
}

resource "azurerm_storage_queue" "stq-converter-output-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "converter-output"
}

resource "azurerm_storage_queue" "stq-thumbnailer-input-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "thumbnailer-input"
}

resource "azurerm_storage_queue" "stq-thumbnailer-output-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "thumbnailer-output"
}

resource "azurerm_storage_container" "stc-converter-input-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "converter-input"
}

resource "azurerm_storage_container" "stc-converter-output-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "converter-output"
}

resource "azurerm_storage_container" "stc-thumbnailer-input-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "thumbnailer-input"
}

resource "azurerm_storage_container" "stc-thumbnailer-output-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "thumbnailer-output"
}

resource "azurerm_storage_queue" "stq-uploader-tg-bot" {
  storage_account_name = azurerm_storage_account.st-tg-bot.name

  name = "uploader"
}

resource "azurerm_service_plan" "asp-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location

  name     = "asp-${var.bot-name}-tg-bot-${var.env}"
  os_type  = "Linux"
  sku_name = "Y1"

  tags = local.tags
}

resource "azurerm_linux_function_app" "func-tg-bot" {
  resource_group_name = azurerm_resource_group.rg-tg-bot.name
  location            = azurerm_resource_group.rg-tg-bot.location

  storage_account_name       = azurerm_storage_account.st-tg-bot.name
  storage_account_access_key = azurerm_storage_account.st-tg-bot.primary_access_key
  service_plan_id            = azurerm_service_plan.asp-tg-bot.id

  name = "func-${var.bot-name}-tg-bot-${var.env}"

  functions_extension_version = "~4"
  builtin_logging_enabled     = false

  site_config {
    application_insights_key = azurerm_application_insights.appi-tg-bot.instrumentation_key
    app_scale_limit          = 10

    application_stack {
      dotnet_version              = "8.0"
      use_dotnet_isolated_runtime = true
    }
  }

  tags = local.tags

  app_settings = merge(
    {
      Telegram__Token  = var.telegram-token
      Telegram__ApiUrl = var.telegram-api-url

      Database__ConnectionString = var.database-connection-string
      Database__Name             = var.database-name

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
}
