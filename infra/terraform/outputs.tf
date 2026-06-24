output "api_url" {
  description = "URL pública del API en Cloud Run (base para el dashboard y /ingest)."
  value       = google_cloud_run_v2_service.api.uri
}

output "worker_service" {
  description = "Nombre del servicio worker (interno)."
  value       = google_cloud_run_v2_service.worker.name
}

output "artifact_registry_repo" {
  description = "Ruta del repo Docker (para docker push de las imágenes en G5b)."
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.atalaya.repository_id}"
}

output "cloudsql_connection_name" {
  description = "Connection name de Cloud SQL (PROJECT:REGION:INSTANCE)."
  value       = google_sql_database_instance.main.connection_name
}

output "redis_host" {
  description = "IP privada de Memorystore (solo alcanzable vía el VPC connector)."
  value       = google_redis_instance.cache.host
}

output "data_lake_bucket" {
  value = google_storage_bucket.data_lake.name
}

output "bigquery_table" {
  value = "${var.project_id}.${google_bigquery_dataset.analytics.dataset_id}.${google_bigquery_table.telemetry_raw.table_id}"
}

output "hosting_default_url" {
  description = "URL por defecto del sitio de Firebase Hosting."
  value       = google_firebase_hosting_site.spa.default_url
}
