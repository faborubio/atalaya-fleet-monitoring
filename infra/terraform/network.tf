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
  # El provider v6 exige fijar el rango de instancias (antes derivaba de throughput). Mínimo del
  # conector = 2; lo acotamos a 3 (e2-micro) para no escalar de más en una ventana de dev.
  min_instances = 2
  max_instances = 3
  depends_on    = [google_project_service.enabled]
}
