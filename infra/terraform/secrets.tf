# Secret Manager: la connection string de Postgres (con la contraseña generada) y el token de ingesta.
# Cloud Run los inyecta como variables de entorno por referencia (las SA tienen secretAccessor).

resource "google_secret_manager_secret" "postgres_conn" {
  secret_id = "atalaya-postgres-conn"
  replication {
    auto {}
  }
  depends_on = [google_project_service.enabled]
}

# Npgsql trata un Host que empieza por '/' como socket unix → el auth proxy de Cloud Run lo monta en
# /cloudsql/<connection_name>. Mismo formato de connection string que el Postgres local salvo el Host.
resource "google_secret_manager_secret_version" "postgres_conn" {
  secret      = google_secret_manager_secret.postgres_conn.id
  secret_data = "Host=/cloudsql/${google_sql_database_instance.main.connection_name};Database=${google_sql_database.atalaya.name};Username=${google_sql_user.atalaya.name};Password=${random_password.db.result}"
}

resource "google_secret_manager_secret" "ingest_token" {
  secret_id = "atalaya-ingest-token"
  replication {
    auto {}
  }
  depends_on = [google_project_service.enabled]
}

resource "google_secret_manager_secret_version" "ingest_token" {
  secret      = google_secret_manager_secret.ingest_token.id
  secret_data = var.ingest_token
}
