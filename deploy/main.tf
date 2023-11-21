terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">=3.37.0"
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
