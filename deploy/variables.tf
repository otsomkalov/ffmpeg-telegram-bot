variable "env" {
  type    = string
  default = "prd"
}

variable "bot-name" {
  type = string
}

variable "telegram-token" {
  type = string
}

variable "telegram-api-url" {
  type = string
}

variable "database-connection-string" {
  type = string
}

variable "database-name" {
  type = string
}

variable "link-regex" {
  type = string
}

variable "mime-types" {
  type = list(string)
}

variable "default-lang" {
  type = string
  default = "en"
}