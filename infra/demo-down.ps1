# demo-down.ps1 — Nivel 2 de la demo de portafolio (ADR-014, ver DEMO.md §2).
# APAGA el stack efímero: destruye SOLO lo que cobra por hora (Cloud Run api/worker, Cloud SQL,
# Memorystore, VPC connector). Deja intacta la base persistente casi gratis (Artifact Registry +
# imágenes, bucket del data lake, BigQuery, Pub/Sub, secrets, Service Accounts, APIs, VPC/subnet),
# así el próximo demo-up es más rápido (no recrea esa base).
$ErrorActionPreference = "Stop"
$TF = "D:\tools\terraform\terraform.exe"
$ROOT = Split-Path $PSScriptRoot -Parent
$REPO = "us-central1-docker.pkg.dev/fabian-portafolio/atalaya"
Set-Location $ROOT

Write-Host "==> Destruyendo recursos facturables (Cloud Run, Cloud SQL, Redis, connector)..." -ForegroundColor Cyan
& $TF -chdir=infra/terraform destroy -auto-approve `
  -var="api_image=$REPO/api:live" -var="worker_image=$REPO/worker:live" `
  -target="google_cloud_run_v2_service_iam_member.api_public" `
  -target="google_cloud_run_v2_service.api" `
  -target="google_cloud_run_v2_service.worker" `
  -target="google_sql_user.atalaya" `
  -target="google_sql_database.atalaya" `
  -target="google_sql_database_instance.main" `
  -target="google_redis_instance.cache" `
  -target="google_vpc_access_connector.connector"
if ($LASTEXITCODE -ne 0) { throw "terraform destroy falló" }

Write-Host "==> Apagado. La base persistente (gratis) sigue en pie." -ForegroundColor Green
Write-Host "    El sitio atalaya-live queda servido pero sin API hasta el próximo demo-up (no lo compartas mientras esté apagado)." -ForegroundColor Yellow
