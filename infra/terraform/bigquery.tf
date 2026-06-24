# Analítica sobre el lake (G4, AUD-024): dataset + external table NDJSON sobre gs://bucket/raw/*.json.
# Equivale a lo que crea scripts/bigquery-setup.mjs, pero declarativo. El endpoint /api/analytics
# consulta esta tabla. El dataset debe estar en la misma ubicación que el bucket.
resource "google_bigquery_dataset" "analytics" {
  dataset_id  = var.bigquery_dataset
  location    = var.bigquery_location
  description = "Analítica de telemetría sobre el data lake (G4)"

  depends_on = [google_project_service.enabled]
}

resource "google_bigquery_table" "telemetry_raw" {
  dataset_id          = google_bigquery_dataset.analytics.dataset_id
  table_id            = "telemetry_raw"
  deletion_protection = false

  external_data_configuration {
    autodetect            = false
    source_format         = "NEWLINE_DELIMITED_JSON"
    source_uris           = ["gs://${google_storage_bucket.data_lake.name}/raw/*.json"]
    ignore_unknown_values = true

    # Esquema explícito (camelCase: el backend serializa con JsonSerializerDefaults.Web).
    schema = jsonencode([
      { name = "eventId", type = "STRING" },
      { name = "deviceId", type = "STRING" },
      { name = "ts", type = "TIMESTAMP" },
      { name = "seq", type = "INTEGER" },
      { name = "lat", type = "FLOAT" },
      { name = "lng", type = "FLOAT" },
      { name = "speedKmh", type = "FLOAT" },
      { name = "headingDeg", type = "FLOAT" },
      { name = "fuelPct", type = "FLOAT" },
      { name = "engineTempC", type = "FLOAT" },
    ])
  }
}
