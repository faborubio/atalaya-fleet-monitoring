# SA mínima: la demo corre en InMemory (sin Postgres/Redis/Pub/Sub/GCS) → solo necesita escribir logs.
resource "google_service_account" "demo" {
  account_id   = "atalaya-demo"
  display_name = "Atalaya Demo (Cloud Run InMemory)"
}

resource "google_project_iam_member" "demo_logging" {
  project = var.project_id
  role    = "roles/logging.logWriter"
  member  = "serviceAccount:${google_service_account.demo.email}"
}

# Cloud Run de la demo (ADR-014, Nivel 1). InMemory + generador de datos + login dev visible.
# scale-to-zero + cpu_idle=true ⇒ la CPU solo se asigna durante un request (el WebSocket del
# dashboard) → el generador produce mientras alguien mira y el servicio se duerme sin tráfico = ~$0.
resource "google_cloud_run_v2_service" "demo" {
  name                = "atalaya-demo-api"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = false

  template {
    service_account = google_service_account.demo.email

    scaling {
      min_instance_count = 0
      max_instance_count = 1
    }

    containers {
      image = var.demo_image

      ports {
        container_port = 8080
      }

      resources {
        cpu_idle = true
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "Telemetry__Transport"
        value = "InMemory"
      }
      env {
        name  = "Demo__Enabled"
        value = "true"
      }
      env {
        name  = "Demo__Devices"
        value = "40"
      }
      env {
        name  = "Demo__IntervalMs"
        value = "1000"
      }
      env {
        name  = "Auth__Mode"
        value = "Dev"
      }
      env {
        name  = "Auth__Issuer"
        value = "atalaya-demo"
      }
      env {
        name  = "Auth__Audience"
        value = "atalaya"
      }
      env {
        name  = "Auth__DevSigningKey"
        value = var.demo_signing_key
      }
      env {
        name  = "Cors__Origins__0"
        value = var.hosting_origin
      }
    }
  }

  # El provider materializa el bloque scaling a nivel de servicio (no lo declaramos) → ignorarlo
  # evita un diff perpetuo (mismo caso que el stack completo, AUD-031).
  lifecycle {
    ignore_changes = [scaling]
  }
}

# La API hace su propia auth (JWT dev); a nivel de Cloud Run se permite invocación pública.
resource "google_cloud_run_v2_service_iam_member" "demo_public" {
  name     = google_cloud_run_v2_service.demo.name
  location = var.region
  role     = "roles/run.invoker"
  member   = "allUsers"
}
