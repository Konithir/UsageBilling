variable "project_name" {
  type    = string
  default = "usagebilling"
}

variable "environment" {
  type    = string
  default = "dev"
}

variable "location" {
  type    = string
  default = "westeurope"
}

variable "database_password" {
  type      = string
  sensitive = true
}

variable "stripe_secret_key" {
  type      = string
  sensitive = true
  default   = ""
}

variable "api_image" {
  type    = string
  default = "ghcr.io/example/usage-billing-api:latest"
}

variable "worker_image" {
  type    = string
  default = "ghcr.io/example/usage-billing-worker:latest"
}

variable "web_image" {
  type    = string
  default = "ghcr.io/example/usage-billing-web:latest"
}