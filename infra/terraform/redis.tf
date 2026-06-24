# Memorystore for Redis (ADR-002): backplane de SignalR + dedup + pub/sub de deltas. Reemplaza el
# Redis en Docker. Tier BASIC (sin réplica) = el más barato; ⚠️ cobra por hora encendido (teardown G6).
resource "google_redis_instance" "cache" {
  name               = "atalaya-redis"
  tier               = "BASIC"
  memory_size_gb     = var.redis_memory_gb
  region             = var.region
  redis_version      = "REDIS_7_0"
  authorized_network = google_compute_network.vpc.id

  depends_on = [google_project_service.enabled]
}
