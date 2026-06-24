# Service accounts de runtime de mínimo privilegio (uno por servicio). Reemplazan las claves de SA
# que se usaron a mano en G3/G4: en Cloud Run las apps usan la identidad del servicio (ADC), sin keys.

# --- API ---------------------------------------------------------------------------------------
resource "google_service_account" "api" {
  account_id   = "atalaya-api"
  display_name = "Atalaya API (Cloud Run)"
}

# Publica telemetría a Pub/Sub (ingesta), lee analítica (BigQuery sobre el lake), Cloud SQL + secrets.
locals {
  api_roles = [
    "roles/pubsub.publisher",    # /ingest → topic
    "roles/bigquery.jobUser",    # lanzar queries (G4)
    "roles/bigquery.dataViewer", # leer la external table (G4)
    "roles/cloudsql.client",     # conectar a Cloud SQL por el auth proxy
    "roles/secretmanager.secretAccessor",
    "roles/logging.logWriter",
    # storage.objectViewer del lake = bucket-scoped en storage.tf (mínimo privilegio).
  ]
}

resource "google_project_iam_member" "api" {
  for_each = toset(local.api_roles)
  project  = var.project_id
  role     = each.value
  member   = "serviceAccount:${google_service_account.api.email}"
}

# --- Worker ------------------------------------------------------------------------------------
resource "google_service_account" "worker" {
  account_id   = "atalaya-worker"
  display_name = "Atalaya Worker (Cloud Run)"
}

# Consume Pub/Sub, escribe el data lake en GCS, Cloud SQL (read models) + secrets.
locals {
  worker_roles = [
    "roles/pubsub.subscriber", # consumir la suscripción
    "roles/cloudsql.client",
    "roles/secretmanager.secretAccessor",
    "roles/logging.logWriter",
  ]
}

resource "google_project_iam_member" "worker" {
  for_each = toset(local.worker_roles)
  project  = var.project_id
  role     = each.value
  member   = "serviceAccount:${google_service_account.worker.email}"
}
