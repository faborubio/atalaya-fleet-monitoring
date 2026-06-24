# Terraform + providers de Atalaya en GCP (G5, ADR-013). Reemplaza el AWS CDK (infra/cdk) como IaC.
# Estado remoto en GCS (el bucket debe existir antes; se pasa por -backend-config en `terraform init`).
# Para validar localmente sin backend: `terraform init -backend=false && terraform validate`.

terraform {
  required_version = ">= 1.6.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
    google-beta = {
      source  = "hashicorp/google-beta"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  backend "gcs" {
    # bucket = "atalaya-tfstate"   # se pasa por -backend-config (bucket=...) en init
    prefix = "atalaya/terraform/state"
  }
}

provider "google" {
  project = var.project_id
  region  = var.region
}

provider "google-beta" {
  project = var.project_id
  region  = var.region
}
