variable "project_id" {
  type        = string
  description = "ID del proyecto GCP (p.ej. fabian-portafolio)."
}

variable "region" {
  type        = string
  description = "Región de despliegue (Cloud Run, Cloud SQL, Memorystore, VPC connector)."
  default     = "us-central1"
}

variable "data_lake_bucket" {
  type        = string
  description = "Nombre del bucket del data lake (debe coincidir con Gcp:Bucket de las apps)."
  default     = "atalaya-datalake"
}

variable "bigquery_dataset" {
  type        = string
  description = "Dataset de BigQuery para la analítica (G4)."
  default     = "atalaya_analytics"
}

variable "bigquery_location" {
  type        = string
  description = "Ubicación del dataset de BigQuery; debe coincidir con la del bucket del lake."
  default     = "US"
}

# Imágenes de contenedor (se publican en Artifact Registry en G5b antes del apply de Cloud Run).
variable "api_image" {
  type        = string
  description = "Imagen completa del API (p.ej. us-central1-docker.pkg.dev/PROJ/atalaya/api:TAG)."
  default     = ""
}

variable "worker_image" {
  type        = string
  description = "Imagen completa del worker."
  default     = ""
}

# Tamaños mínimos (control de costo). Subir conscientemente.
variable "cloudsql_tier" {
  type        = string
  description = "Tier de Cloud SQL (db-f1-micro = el más barato; cobra por hora encendido)."
  default     = "db-f1-micro"
}

variable "redis_memory_gb" {
  type        = number
  description = "Memoria de Memorystore en GB (1 = mínimo Basic; cobra por hora encendido)."
  default     = 1
}

variable "ingest_token" {
  type        = string
  description = "Token de ingesta del borde (X-Ingest-Token). Se guarda en Secret Manager."
  sensitive   = true
  default     = "change-me-ingest-token"
}

variable "cors_origins" {
  type        = list(string)
  description = "Orígenes permitidos del dashboard (dominio de Firebase Hosting). Se completa en G5b."
  default     = []
}

variable "hosting_site_id" {
  type        = string
  description = "ID del sitio de Firebase Hosting (globalmente único, minúsculas/guiones, ≤30 chars)."
  default     = "atalaya-dashboard"
}
