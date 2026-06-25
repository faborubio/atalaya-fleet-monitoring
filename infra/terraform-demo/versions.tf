# IaC del Nivel 1 de la demo de portafolio (ADR-014, ver DEMO.md): un único Cloud Run con la API en
# modo InMemory + generador de datos, scale-to-zero (~$0). Estado SEPARADO del stack completo
# (infra/terraform/) para encender/apagar la demo sin tocar nada más. Backend GCS con prefijo propio.
terraform {
  required_version = ">= 1.5"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }

  backend "gcs" {
    prefix = "atalaya/demo/state"
    # bucket se pasa con -backend-config="bucket=atalaya-tfstate"
  }
}

provider "google" {
  project = var.project_id
  region  = var.region
}
