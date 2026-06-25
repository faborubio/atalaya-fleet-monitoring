output "demo_api_url" {
  description = "URL pública de la API de demo (Cloud Run). Va en demoApiConfig del SPA + CORS."
  value       = google_cloud_run_v2_service.demo.uri
}
