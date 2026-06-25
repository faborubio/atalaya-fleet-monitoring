# demo-up.ps1 — Nivel 2 de la demo de portafolio (ADR-014, ver DEMO.md §2).
# ENCIENDE el stack EFÍMERO completo en GCP (Pub/Sub + Cloud SQL + Memorystore + VPC connector +
# Cloud Run api/worker + BigQuery) y publica el SPA "live". Para una demo/entrevista en vivo.
# ⚠️ Cobra mientras esté encendido (~US$0.5–2/h). Apaga con demo-down.ps1 al terminar.
#
# Uso:   pwsh infra/demo-up.ps1            (reconstruye y publica imágenes)
#        pwsh infra/demo-up.ps1 -SkipBuild (reusa api:live/worker:live ya publicadas — más rápido)
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
$TF = "D:\tools\terraform\terraform.exe"
$ROOT = Split-Path $PSScriptRoot -Parent   # infra/ -> raíz del repo
$PROJECT = "fabian-portafolio"
$REPO = "us-central1-docker.pkg.dev/$PROJECT/atalaya"
$API_IMG = "$REPO/api:live"
$WORKER_IMG = "$REPO/worker:live"
Set-Location $ROOT

if (-not $SkipBuild) {
  Write-Host "==> Construyendo y publicando imágenes (api:live, worker:live)..." -ForegroundColor Cyan
  $token = gcloud auth application-default print-access-token
  $token | docker login -u oauth2accesstoken --password-stdin https://us-central1-docker.pkg.dev | Out-Null
  docker build -f apps/api/Dockerfile    -t $API_IMG    .
  docker build -f apps/worker/Dockerfile -t $WORKER_IMG .
  docker push $API_IMG
  docker push $WORKER_IMG
}

Write-Host "==> terraform apply (Cloud SQL tarda ~10 min)..." -ForegroundColor Cyan
& $TF -chdir=infra/terraform apply -auto-approve -var="api_image=$API_IMG" -var="worker_image=$WORKER_IMG"
if ($LASTEXITCODE -ne 0) { throw "terraform apply falló" }

$apiUrl = (& $TF -chdir=infra/terraform output -raw api_url)
Write-Host "==> API en: $apiUrl" -ForegroundColor Green

Write-Host "==> Build + deploy del SPA live (login real OIDC)..." -ForegroundColor Cyan
npx nx build atalaya-web --configuration=production
firebase deploy --only hosting:atalaya-live --project $PROJECT --non-interactive

Write-Host "==> Smoke /health/ready..." -ForegroundColor Cyan
$ready = $false
for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
  try { if ((Invoke-WebRequest -Uri "$apiUrl/health/ready" -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200) { $ready = $true } } catch { Start-Sleep 10 }
}
Write-Host ("==> ready: " + ($(if ($ready) { "200 OK" } else { "no respondió (revisar logs de Cloud Run)" })))

Write-Host ""
Write-Host "==> LISTO. SPA:  https://atalaya-live.web.app" -ForegroundColor Green
Write-Host "          API:  $apiUrl" -ForegroundColor Green
Write-Host "    Login: usuario de prueba de Identity Platform (atalaya-test@atalaya.dev)." -ForegroundColor Yellow
Write-Host "    ⚠️ Está COBRANDO. Apaga al terminar:  pwsh infra/demo-down.ps1" -ForegroundColor Yellow
