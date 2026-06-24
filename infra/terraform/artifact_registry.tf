# Repositorio Docker en Artifact Registry: aloja las imágenes de api y worker que despliega Cloud Run.
# Las imágenes se construyen y publican en G5b (docker build/push) antes del apply de los servicios.
resource "google_artifact_registry_repository" "atalaya" {
  location      = var.region
  repository_id = "atalaya"
  description   = "Imágenes de contenedor de Atalaya (api, worker)"
  format        = "DOCKER"

  depends_on = [google_project_service.enabled]
}
