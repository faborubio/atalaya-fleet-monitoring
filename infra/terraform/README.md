# Infra GCP de Atalaya (Terraform) — G5

IaC del despliegue real en GCP (ADR-013). Reemplaza el AWS CDK (`infra/cdk/`). Define **todo** el
plano de control: mensajería, datos, cómputo y hosting.

> **Estado:** **G5a** (escrito + `terraform validate` ✅, imágenes que construyen) está hecho.
> El **`apply` real (G5b)** cobra (Cloud SQL + Memorystore + Cloud Run always-on) → se ejecuta en una
> ventana acotada y se hace **teardown** (G6). Ver §Costo.

## Qué crea

| Archivo | Recursos |
|---|---|
| `apis.tf` | Habilita las APIs necesarias |
| `artifact_registry.tf` | Repo Docker para las imágenes api/worker |
| `service_accounts.tf` | SAs de runtime (api, worker) de mínimo privilegio + IAM de proyecto |
| `pubsub.tf` | Topic + DLQ + suscripción con `DeadLetterPolicy` + IAM del *service agent* de Pub/Sub |
| `storage.tf` | Bucket del data lake (lifecycle frío) + IAM bucket-scoped |
| `bigquery.tf` | Dataset + external table NDJSON sobre el lake (G4) |
| `sql.tf` | Cloud SQL Postgres (tier mínimo) + db + user |
| `redis.tf` | Memorystore Redis (BASIC) |
| `network.tf` | VPC + subnet + Serverless VPC Access connector (Cloud Run → Redis) |
| `secrets.tf` | Secret Manager: connection string de Postgres + token de ingesta |
| `cloudrun.tf` | API (público) + Worker (interno), always-on, Cloud SQL por socket + Redis por VPC |
| `firebase.tf` | Sitio de Firebase Hosting (el contenido se sube con `firebase deploy`) |

## Verificación local (G5a, $0)

```powershell
terraform fmt
terraform init -backend=false      # descarga providers, sin estado remoto
terraform validate                 # ✅ valida HCL + esquemas de provider
```

## Despliegue (G5b, con costo)

```powershell
# 0) Autenticación: ADC con una SA con permisos de admin de los recursos (o `gcloud auth ...`).
$env:GOOGLE_APPLICATION_CREDENTIALS="C:\ruta\sa-admin.json"

# 1) Estado remoto: crear una vez el bucket de tfstate y `init` apuntándolo.
#    gsutil mb -l us-central1 gs://atalaya-tfstate    (o por consola)
terraform init -backend-config="bucket=atalaya-tfstate"

# 2) Construir y publicar imágenes en Artifact Registry (el repo lo crea el primer apply parcial,
#    o créalo aparte): tag = us-central1-docker.pkg.dev/<proj>/atalaya/{api,worker}:<v>
docker build -f apps/api/Dockerfile    -t <repo>/api:v1 .
docker build -f apps/worker/Dockerfile -t <repo>/worker:v1 .
docker push <repo>/api:v1 ; docker push <repo>/worker:v1

# 3) Plan + apply con las imágenes y el dominio de la SPA.
terraform plan  -var="api_image=<repo>/api:v1" -var="worker_image=<repo>/worker:v1"
terraform apply -var="api_image=<repo>/api:v1" -var="worker_image=<repo>/worker:v1"

# 4) SPA → Firebase Hosting (build de Angular apuntando al api_url del output):
#    firebase deploy --only hosting
```

## Costo y teardown (⚠️ obligatorio)

- **Escalan a cero / casi gratis:** Pub/Sub, GCS, BigQuery (free-tier), Artifact Registry, Secret Manager.
- **Cobran por hora encendidos:** **Cloud SQL** (~US$8–30), **Memorystore BASIC** (~US$35) + VPC connector,
  **Cloud Run always-on** ×2 (~US$20–40). Estimado **~US$70–120/mes** si se deja encendido.
- **Teardown** (G6): `terraform destroy`. El bucket del lake y el dataset tienen protección
  (`force_destroy=false`, `deletion_protection`); bórralos a conciencia si quieres limpiar también los datos.
- Mantener **Budget + Alert** (G0) activo durante cualquier ventana de `apply`.
