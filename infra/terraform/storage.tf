# Data lake de eventos crudos (ADR-007), equivalente GCS del bucket S3 del CDK. Objetos NDJSON bajo
# raw/yyyy/MM/dd/ (G2/G4); lifecycle a clases frías para abaratar el histórico.
resource "google_storage_bucket" "data_lake" {
  name     = var.data_lake_bucket
  location = var.bigquery_location # multi-región US por defecto; debe coincidir con el dataset
  project  = var.project_id

  uniform_bucket_level_access = true
  force_destroy               = false # en prod el lake no se borra al destruir por accidente

  lifecycle_rule {
    condition { age = 30 }
    action {
      type          = "SetStorageClass"
      storage_class = "NEARLINE"
    }
  }
  lifecycle_rule {
    condition { age = 90 }
    action {
      type          = "SetStorageClass"
      storage_class = "COLDLINE"
    }
  }

  depends_on = [google_project_service.enabled]
}

# El worker escribe el lake; el API (vía BigQuery) solo lo lee. Scoping a bucket = mínimo privilegio.
resource "google_storage_bucket_iam_member" "worker_writer" {
  bucket = google_storage_bucket.data_lake.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_storage_bucket_iam_member" "api_reader" {
  bucket = google_storage_bucket.data_lake.name
  role   = "roles/storage.objectViewer"
  member = "serviceAccount:${google_service_account.api.email}"
}
