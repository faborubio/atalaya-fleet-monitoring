# Red privada para que Cloud Run alcance Memorystore (Redis), que solo expone IP privada en la VPC.
# Cloud Run llega vía un Serverless VPC Access connector (egress a rangos privados).
resource "google_compute_network" "vpc" {
  name                    = "atalaya-vpc"
  auto_create_subnetworks = false
  depends_on              = [google_project_service.enabled]
}

resource "google_compute_subnetwork" "subnet" {
  name          = "atalaya-subnet"
  ip_cidr_range = "10.10.0.0/24"
  region        = var.region
  network       = google_compute_network.vpc.id
}

# Conector serverless: une Cloud Run (serverless) con la VPC. Necesita un /28 sin solapamiento.
resource "google_vpc_access_connector" "connector" {
  name          = "atalaya-connector"
  region        = var.region
  network       = google_compute_network.vpc.name
  ip_cidr_range = "10.8.0.0/28"
  depends_on    = [google_project_service.enabled]
}
