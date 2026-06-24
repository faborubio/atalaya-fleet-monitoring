# Cloud SQL for PostgreSQL (read models + telemetría particionada + alert_incidents). Reemplaza el
# Postgres en Docker; las apps solo cambian la connection string (sin código, como anticipó G2).
# Cloud Run conecta por el auth proxy (socket unix /cloudsql/...), no por IP pública directa.
# ⚠️ Cobra por hora encendido (teardown G6). Tier mínimo y sin alta disponibilidad para abaratar.
resource "random_password" "db" {
  length  = 24
  special = false
}

resource "google_sql_database_instance" "main" {
  name                = "atalaya-postgres"
  database_version    = "POSTGRES_16"
  region              = var.region
  deletion_protection = false # dev; en prod = true

  settings {
    tier              = var.cloudsql_tier
    availability_type = "ZONAL"
    disk_size         = 10
    disk_autoresize   = true

    ip_configuration {
      ipv4_enabled = true # el auth proxy de Cloud Run usa esta vía; sin redes autorizadas abiertas
    }

    backup_configuration {
      enabled = false # dev: ahorra costo; en prod = true
    }
  }

  depends_on = [google_project_service.enabled]
}

resource "google_sql_database" "atalaya" {
  name     = "atalaya"
  instance = google_sql_database_instance.main.name
}

resource "google_sql_user" "atalaya" {
  name     = "atalaya"
  instance = google_sql_database_instance.main.name
  password = random_password.db.result
}
