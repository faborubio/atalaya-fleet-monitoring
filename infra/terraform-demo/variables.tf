variable "project_id" {
  type        = string
  description = "ID del proyecto GCP (p.ej. fabian-portafolio)."
}

variable "region" {
  type        = string
  description = "Región de Cloud Run."
  default     = "us-central1"
}

variable "demo_image" {
  type        = string
  description = "Imagen de la API para la demo (p.ej. us-central1-docker.pkg.dev/PROJ/atalaya/api:demo)."
}

variable "hosting_origin" {
  type        = string
  description = "Origen del SPA de demo (Firebase Hosting) permitido por CORS."
  default     = "https://atalaya-demo.web.app"
}

variable "demo_signing_key" {
  type        = string
  description = "Clave HS256 para los tokens dev de la demo. NO es secreto: solo firma tokens de una demo con datos sintéticos."
  default     = "atalaya-demo-hs256-key-public-not-a-secret-1234567890"
}
