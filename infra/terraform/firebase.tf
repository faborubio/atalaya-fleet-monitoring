# Firebase Hosting para la SPA Angular. Terraform provisiona el *sitio*; el contenido (build del
# dashboard) se publica con la CLI `firebase deploy` en G5b (Terraform no gestiona el bundle).
# Nota: el proyecto ya tiene Firebase habilitado (G3, Identity Platform/Auth), por eso NO se gestiona
# google_firebase_project aquí (evita un "already exists" en el apply). Si fuera un proyecto nuevo,
# habría que añadirlo y crear el sitio tras él.
resource "google_firebase_hosting_site" "spa" {
  provider = google-beta
  project  = var.project_id
  site_id  = var.hosting_site_id

  depends_on = [google_project_service.enabled]
}
