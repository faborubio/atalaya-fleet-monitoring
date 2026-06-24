# Mensajería del camino caliente (ADR-001/006, equivalente GCP del SNS→SQS+DLQ del CDK). El API
# publica al topic; el worker consume la suscripción (pull). DLQ por DeadLetterPolicy tras 5 intentos.

data "google_project" "this" {}

resource "google_pubsub_topic" "telemetry" {
  name       = "atalaya-telemetry"
  depends_on = [google_project_service.enabled]
}

resource "google_pubsub_topic" "dlq" {
  name       = "atalaya-telemetry-dlq"
  depends_on = [google_project_service.enabled]
}

resource "google_pubsub_subscription" "telemetry" {
  name  = "atalaya-telemetry-sub"
  topic = google_pubsub_topic.telemetry.id

  ack_deadline_seconds = 60

  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dlq.id
    max_delivery_attempts = 5
  }

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }
}

# La DLQ exige IAM al *service agent* de Pub/Sub (no al SA del worker): publicar en la DLQ y
# reconocer en la suscripción de origen. Sin esto, los mensajes envenenados no se enrutan (gap que
# G1/AUD-021 anotó como pendiente de Terraform).
locals {
  pubsub_agent = "serviceAccount:service-${data.google_project.this.number}@gcp-sa-pubsub.iam.gserviceaccount.com"
}

resource "google_pubsub_topic_iam_member" "dlq_publisher" {
  topic  = google_pubsub_topic.dlq.id
  role   = "roles/pubsub.publisher"
  member = local.pubsub_agent
}

resource "google_pubsub_subscription_iam_member" "dlq_subscriber" {
  subscription = google_pubsub_subscription.telemetry.id
  role         = "roles/pubsub.subscriber"
  member       = local.pubsub_agent
}
