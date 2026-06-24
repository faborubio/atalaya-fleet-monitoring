# DEPLOY.md — Despliegue

Cómo se levanta Atalaya en **local** y cómo se despliega en **AWS**. Este documento es
vivo: cada componente nuevo añade aquí sus pasos de arranque/despliegue.

> **Estado actual (2026-06-21):** todavía no hay artefactos desplegables. Esta guía
> describe el destino según el [SAD](./SAD-Atalaya.md) y se irá rellenando con comandos
> reales y verificados a medida que exista código. Lo no implementado se marca con ⛔.

---

## 0. Prerrequisitos

| Herramienta | Versión | Para qué | Estado |
|---|---|---|---|
| Node.js | ≥ 20 | Frontend Angular, Nx, simulador, CDK | ✅ v24.15 |
| npm | ≥ 10 | Gestor de paquetes | ✅ 11.14 |
| git | ≥ 2.40 | Control de versiones | ✅ 2.51 |
| .NET SDK | 8.x | Backend (Minimal API + Workers) | ⛔ falta ([TS-001](./TROUBLESHOOTING.md#ts-001--no-hay-net-sdk-solo-runtime)) |
| Docker Desktop | reciente | LocalStack, Redis, PostgreSQL, Testcontainers | ⛔ falta ([TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible)) |
| AWS CLI | 2.x | Interactuar con AWS / LocalStack | ⛔ por instalar |
| AWS CDK | 2.x | Infraestructura como código | ⛔ por instalar (`npm i -g aws-cdk`) |

---

## 1. Desarrollo local

### 1.1 Frontend (Angular) — ✅ disponible

```bash
npm install
npm start                     # = nx serve atalaya-web → http://localhost:4200
```

### 1.2 Simulador de telemetría — ✅ disponible (Fase 0)

```bash
npx nx build simulator
# En seco (solo métricas en consola), útil sin backend:
node dist/apps/simulator/main.js --rate 2000 --devices 100 --duration 10
# Contra la ingesta (Fase 1), apuntando al endpoint:
INGEST_URL=http://localhost:3000/ingest node dist/apps/simulator/main.js --rate 5000 --devices 500
```
Flags: `--rate` (ev/s), `--devices`, `--duration` (s, 0 = ∞), `--url` (o `INGEST_URL`).

### 1.3 Backend .NET — ✅ disponible (modo dev sin Docker)

```bash
dotnet build Atalaya.sln                 # compila contracts + api + worker + tests
nx serve api                             # = dotnet run apps/api → http://localhost:3000
#   endpoints: POST /ingest · GET /api/devices · GET /health · hub /hubs/telemetry
nx serve worker                          # esqueleto (consumo SQS pendiente Docker, TS-002)
nx test api-tests                        # test de integración del camino caliente
```

Prueba end-to-end del camino caliente (sin Docker):
```bash
nx serve api
# en otra terminal:
node dist/apps/simulator/main.js --rate 1000 --devices 50 --duration 5 --url http://localhost:3000/ingest
curl http://localhost:3000/api/devices   # read model device_state poblado
```

### 1.5 Pipeline real completo (modo Aws, requiere infra arriba)

```bash
docker compose -f infra/docker-compose.yml up -d   # LocalStack + Redis + Postgres
npx nx serve api        # modo Aws (Development): publica a SNS, reenvía Redis→SignalR
npx nx serve worker     # consume SQS → dedup(Redis) → Postgres → publica deltas a Redis
npm start               # dashboard Angular en http://localhost:4200 (se conecta al hub)
# inyectar carga (modo Aws exige token de ingesta):
node dist/apps/simulator/main.js --rate 2000 --devices 100 --duration 30 \
  --url http://localhost:3000/ingest --token dev-ingest-token
```
Flujo: `/ingest → SNS → SQS → worker → dedup → Postgres → Redis → API → SignalR → dashboard`.
Seguridad: `/ingest` valida `X-Ingest-Token` (config `Ingest:Token`) y aplica rate limiting
(`Ingest:RatePerSecond`, 429 al exceder).

### 1.6 Prueba de carga (k6 vía Docker)

```bash
# La API debe estar arriba (modo Aws). k6 corre en contenedor y apunta al host.
Get-Content infra/load/ingest.js | docker run --rm -i `
  -e INGEST_URL=http://host.docker.internal:3000/ingest `
  -e INGEST_TOKEN=dev-ingest-token grafana/k6 run -
```
> **Hallazgo (AUD-009):** contra **LocalStack** el techo es ~1.000–1.300 ev/s — el cuello
> de botella es el `PublishAsync` síncrono a SNS por request (la cola SQS queda en 0; el
> worker con `Aws:Consumers` en paralelo sí drena). Para acercarse a 5.000 ev/s: desacoplar
> el publish (batch en segundo plano) y/o medir contra AWS real.

> **Transporte.** `Telemetry:Transport` = `Aws` en Development (pipeline real) o `InMemory`
> (procesa en la API, sin Docker; lo usan los tests). Las interfaces
> `ITelemetryPublisher`/`IDeviceStateRepository`/`IEventDeduplicator`/`ITelemetryBroadcaster`
> permiten alternar sin reescribir la lógica.

### 1.4 Infra local con LocalStack — ✅ disponible

```bash
docker compose -f infra/docker-compose.yml up -d    # LocalStack (SNS/SQS/S3) + Redis + Postgres
docker compose -f infra/docker-compose.yml ps       # los 3 deben estar healthy
```
Recursos (SNS/SQS/DLQ/S3 + suscripción) los crea automáticamente
`infra/localstack/init/01-resources.sh` al arrancar. Detalle en
[infra/README.md](./infra/README.md).

> Endpoints dev: AWS `http://localhost:4566` · Redis `localhost:6379` ·
> Postgres `localhost:5432` (atalaya/atalaya).
> El objetivo (ADR-009) es definir estos recursos con **AWS CDK**; hoy se crean con
> `awslocal` para tener el pipeline ya corriendo.

### 1.7 Analítica con BigQuery (modo Gcp, G4) — ✅ disponible

No hay emulador de BigQuery → se valida contra **BigQuery real** (free-tier 1 TB consultas/mes
cubre dev; mantener Budget+Alert). El data lake en GCS se escribe en **NDJSON** (un evento por
línea), que la external table lee directamente.

```bash
# 0) Consola GCP (una vez): habilitar BigQuery API + Budget/Alert. La SA de consulta necesita
#    bigquery.jobUser + bigquery.dataViewer + storage.objectViewer (sobre el bucket del lake).
# 1) Poblar el lake real (worker en modo Gcp contra GCS real, no fake-gcs):
$env:GOOGLE_APPLICATION_CREDENTIALS="C:\ruta\sa-key.json"
$env:Telemetry__Transport="Gcp"; $env:Gcp__ProjectId="fabian-portafolio"; $env:Gcp__StorageEmulatorHost=""
npx nx serve worker      # auto-crea el bucket atalaya-datalake si falta
# (API en modo Gcp + simulador, como en §1.5 pero con Transport=Gcp)

# 2) Crear dataset + external table (idempotente; BQ_LOCATION = ubicación del bucket):
$env:BQ_LOCATION="US"
node scripts/bigquery-setup.mjs fabian-portafolio atalaya-datalake atalaya_analytics "C:\ruta\sa-key.json"

# 3) API con analítica activa + consulta:
$env:Gcp__DatasetId="atalaya_analytics"; npx nx serve api
curl "http://localhost:3000/api/analytics/devices?minutes=60&limit=10"
```
> **Cost guard (AUD-024):** la external table no tiene poda de particiones (layout `yyyy/MM/dd`,
> no hive) → cada query escanea todo el lake. `Gcp:AnalyticsMaxBytesBilled` (default 1 GB) hace que
> BigQuery **rechace** una consulta que exceda el tope en vez de facturarla.

---

## 2. Despliegue en AWS ⛔ *(planificado, no implementado)*

Toda la infra se define con **AWS CDK** (ADR-009). Nada se crea a mano en la consola.

### 2.1 Recursos que define el CDK

- **SNS** (fan-out de ingesta) + **SQS** (buffer, standard y FIFO donde el orden importa).
- **Lambda** de ingesta tras **API Gateway** (auth + validación + rate limiting).
- **S3** data lake con lifecycle (cold/archive) + **Athena** para histórico.
- **ElastiCache/Redis** (set de dedup + backplane SignalR).
- **RDS/PostgreSQL** (read models + telemetría particionada).
- **IAM** con mínimo privilegio; **Secrets Manager/SSM** para secretos.

### 2.2 Flujo de despliegue

```bash
cdk diff      # revisar cambios contra el entorno
cdk deploy    # aplicar infra
```

### 2.3 Pipeline CI/CD (objetivo, SAD §11)

```
install → lint (incl. reglas RxJS) → typecheck → unit (front + .NET) → marble tests
        → build front + back → test integración (LocalStack) → E2E
        → deploy infra (CDK diff/deploy) → smoke + carga ligera
```

- Monorepo **Nx** con caché de tareas afectadas.
- Entornos efímeros por PR donde sea viable.
- Versionado semántico de la API; contratos OpenAPI validados front↔back.

---

## 3. Smoke test post-despliegue ⛔

Checklist mínimo tras cada deploy (se rellenará con comandos reales):

- [ ] El simulador inyecta eventos y la cola los recibe (profundidad sube/baja).
- [ ] El worker consume, deduplica y actualiza `device_state`.
- [ ] El dashboard recibe deltas por SignalR (latencia evento→pantalla < 1.5 s P95).
- [ ] Una regla de umbral dispara una alerta visible en la UI.
- [ ] Tras reiniciar un worker, no hay pérdida (la cola retiene).

---

## 4. Rollback ⛔

- Infra: `cdk deploy` de la versión anterior (estado en CloudFormation).
- App: redeploy del artefacto previo (imágenes/versiones etiquetadas).
- Datos: el S3 data lake es inmutable (fuente de verdad fría); los read models se pueden
  reconstruir reproyectando desde los eventos crudos.

---

> A medida que cada pieza exista, sustituir los ⛔ por comandos **probados** y enlazar la
> auditoría correspondiente en [AUDIT.md](./AUDIT.md).
