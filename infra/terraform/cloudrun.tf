# Cómputo: API (público, SignalR) y Worker (interno, pull) en Cloud Run v2 (ADR-008, reemplaza EC2/ECS).
# Ambos always-on (min_instance_count=1, cpu_idle=false): el API por los forwarders Redis + WebSocket;
# el worker por el streaming pull continuo de Pub/Sub. Cloud SQL por socket unix; Redis por VPC connector.

# --- API ---------------------------------------------------------------------------------------
resource "google_cloud_run_v2_service" "api" {
  name                = "atalaya-api"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = false

  template {
    service_account = google_service_account.api.email

    scaling {
      min_instance_count = 1
      max_instance_count = 3
    }

    vpc_access {
      connector = google_vpc_access_connector.connector.id
      egress    = "PRIVATE_RANGES_ONLY" # solo el tráfico a rangos privados (Redis) pasa por la VPC
    }

    volumes {
      name = "cloudsql"
      cloud_sql_instance {
        instances = [google_sql_database_instance.main.connection_name]
      }
    }

    containers {
      image = var.api_image

      ports {
        container_port = 8080
      }

      resources {
        cpu_idle = false # CPU siempre asignada (forwarders en background + SignalR)
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
      }

      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "Telemetry__Transport"
        value = "Gcp"
      }
      env {
        name  = "Gcp__ProjectId"
        value = var.project_id
      }
      env {
        name  = "Gcp__DatasetId"
        value = var.bigquery_dataset
      }
      env {
        name  = "Auth__Mode"
        value = "Oidc"
      }
      env {
        name  = "Auth__ProjectId"
        value = var.project_id
      }
      env {
        name  = "ConnectionStrings__Redis"
        value = "${google_redis_instance.cache.host}:${google_redis_instance.cache.port}"
      }
      # Orígenes CORS de la SPA (Firebase Hosting). Se completan en G5b con el dominio real.
      dynamic "env" {
        for_each = { for idx, origin in var.cors_origins : idx => origin }
        content {
          name  = "Cors__Origins__${env.key}"
          value = env.value
        }
      }
      env {
        name = "ConnectionStrings__Postgres"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.postgres_conn.secret_id
            version = "latest"
          }
        }
      }
      env {
        name = "Ingest__Token"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.ingest_token.secret_id
            version = "latest"
          }
        }
      }
    }
  }

  # El provider 6.x materializa el bloque `scaling` a NIVEL DE SERVICIO (modo automático,
  # min/manual_instance_count=0) que no declaramos —escalamos por `template.scaling`—; ignorarlo
  # evita un diff perpetuo que no converge. El escalado real lo fija el template.
  lifecycle {
    ignore_changes = [scaling]
  }

  depends_on = [
    google_project_iam_member.api,
    google_secret_manager_secret_version.postgres_conn,
    google_secret_manager_secret_version.ingest_token,
  ]
}

# El API hace su propia auth (JWT/OIDC); a nivel de Cloud Run se permite invocación pública.
resource "google_cloud_run_v2_service_iam_member" "api_public" {
  name     = google_cloud_run_v2_service.api.name
  location = var.region
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# --- Worker ------------------------------------------------------------------------------------
resource "google_cloud_run_v2_service" "worker" {
  name                = "atalaya-worker"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_INTERNAL_ONLY" # no recibe tráfico externo (solo pull + health)
  deletion_protection = false

  template {
    service_account = google_service_account.worker.email

    # Un solo consumidor: la caché de incidentes abiertos es coherente en 1 instancia (AUD-017).
    scaling {
      min_instance_count = 1
      max_instance_count = 1
    }

    vpc_access {
      connector = google_vpc_access_connector.connector.id
      egress    = "PRIVATE_RANGES_ONLY"
    }

    volumes {
      name = "cloudsql"
      cloud_sql_instance {
        instances = [google_sql_database_instance.main.connection_name]
      }
    }

    containers {
      image = var.worker_image

      ports {
        container_port = 8080 # WorkerHealthService escucha en $PORT para la sonda de arranque
      }

      resources {
        cpu_idle = false # pull continuo de Pub/Sub
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
      }

      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }

      env {
        name  = "DOTNET_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "Telemetry__Transport"
        value = "Gcp"
      }
      env {
        name  = "Gcp__ProjectId"
        value = var.project_id
      }
      env {
        name  = "Gcp__Bucket"
        value = var.data_lake_bucket
      }
      env {
        name  = "ConnectionStrings__Redis"
        value = "${google_redis_instance.cache.host}:${google_redis_instance.cache.port}"
      }
      env {
        name = "ConnectionStrings__Postgres"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.postgres_conn.secret_id
            version = "latest"
          }
        }
      }
    }
  }

  # Igual que el API: ignora el bloque `scaling` de servicio que inyecta el provider (el escalado
  # real lo fija template.scaling: una sola instancia por la caché de incidentes, AUD-017).
  lifecycle {
    ignore_changes = [scaling]
  }

  depends_on = [
    google_project_iam_member.worker,
    google_secret_manager_secret_version.postgres_conn,
  ]
}
