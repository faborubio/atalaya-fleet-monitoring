# CLAUDE.md — Contexto del proyecto Atalaya

> Este archivo es el **ancla de contexto** entre sesiones. Léelo al empezar cualquier
> sesión nueva para retomar sin perder el hilo. Manténlo actualizado: cuando cambie el
> estado real del proyecto, actualiza aquí.

---

## 1. Qué es Atalaya

Plataforma de **monitoreo y alertamiento de flota/IoT en tiempo real**. Ingiere telemetría
de miles de dispositivos, mantiene proyecciones en vivo y dispara alertas en sub-segundos.
Es **full-stack con foco frontend** (Angular): el backend existe para que el frontend en
tiempo real tenga carga real que estresar. Proyecto de portafolio orientado a **Angular/.NET/
GCP event-driven con despliegue real**.

> **⚠️ Pivote de nube (2026-06-23, [ADR-013](./SAD-Atalaya.md) / [AUD-020](./AUDIT.md)):** el target
> cloud pasó de **AWS → GCP**. La fase previa (Fases 0→3) está implementada contra **AWS/LocalStack**
> y se conserva; lo que viene se despliega en **GCP** (Pub/Sub, Cloud Storage, BigQuery, Identity
> Platform, Cloud Run, Cloud SQL, Memorystore) con emuladores locales para no gastar. **Nada está
> migrado aún**; el roadmap es G0…G6 (ver §7). Las menciones a AWS abajo son el estado actual real.

**Documento rector:** [SAD-Atalaya.md](./SAD-Atalaya.md) — léelo; cada decisión es un ADR
con contexto y trade-offs. Si una decisión nueva surge, se añade como ADR al SAD.

**Repositorio:** https://github.com/faborubio/atalaya-fleet-monitoring (público, rama `main`).
El remoto `origin` usa **HTTPS** (autenticado vía `gh`); no hay clave SSH cargada en esta máquina.

## 2. Quién

- **Autor:** Fabián Rubio — Full Stack, foco Frontend/Angular.
- **Idioma de trabajo:** español (docs y conversación).

## 3. Reglas de trabajo (acordadas)

- **No delirar ni inventar.** Si algo no está hecho o está bloqueado, se dice. Nada de
  avances ficticios.
- **Si hay duda, preguntar** antes de asumir.
- **Si surge una idea mejor durante la construcción, proponerla.**
- **Documentación viva:** cada fase/cambio se audita en [AUDIT.md](./AUDIT.md); los errores
  resueltos se registran en [TROUBLESHOOTING.md](./TROUBLESHOOTING.md) para no repetirlos.
- Entorno: **Windows 11**, shell **PowerShell** (también Bash disponible). Rutas Windows.

## 4. Mapa de documentos

| Archivo | Para qué |
|---|---|
| [SAD-Atalaya.md](./SAD-Atalaya.md) | Arquitectura rectora (ADR-001…013, NFRs, roadmap). ADR-013 = pivote a GCP |
| [README.md](./README.md) | Visión, stack, estructura, cómo empezar |
| [AUDIT.md](./AUDIT.md) | Bitácora de auditorías por fase/cambio |
| [DEPLOY.md](./DEPLOY.md) | Despliegue local (LocalStack hoy; emuladores GCP en el pivote) y nube (CDK→Terraform) |
| [TROUBLESHOOTING.md](./TROUBLESHOOTING.md) | Errores encontrados y soluciones |
| **CLAUDE.md** | Este archivo: contexto entre sesiones |

## 5. Estado actual

**Fecha de actualización:** 2026-06-24
**Fase:** 1 + 1.5 + ingesta desacoplada ([AUD-010](./AUDIT.md)) + **Fase 2 completa** (alertas
[AUD-011](./AUDIT.md) + camino frío [AUD-012](./AUDIT.md)) + **productivización** (CDK
[AUD-013](./AUDIT.md) + viewport [AUD-014](./AUDIT.md)) + **Fase 2.5 calidad de datos**
([AUD-016](./AUDIT.md) + [AUD-017](./AUDIT.md)) + **Fase 3 endurecimiento operativo**
([AUD-018](./AUDIT.md)) + **Fase 3 seguridad: auth de lecturas OIDC/JWT** ([AUD-019](./AUDIT.md)).
**El producto (en AWS/LocalStack) está completo.** **Pivote a GCP en marcha** ([AUD-020](./AUDIT.md),
roadmap G0…G6): **G1 (Pub/Sub)** ([AUD-021](./AUDIT.md)) y **G2 (data lake en Cloud Storage)**
([AUD-022](./AUDIT.md)) verificados contra emuladores; **G3 (auth OIDC real con Identity Platform)**
([AUD-023](./AUDIT.md)) y **G4 (analítica con BigQuery sobre el data lake)** ([AUD-024](./AUDIT.md))
verificados contra el proyecto **real `fabian-portafolio`**. **G5a (IaC con Terraform + Dockerfiles)**
([AUD-025](./AUDIT.md)) escrito y validado (`terraform validate` + imágenes que construyen, $0).
Lo siguiente: **G5b** (apply real + E2E en la nube + teardown).

### Hecho
- ✅ SAD v1.0.1 (ADR-001…011) + docs base. Repo en GitHub (12+ commits).
- ✅ **Monorepo Nx 21.6.11** (Angular 20.3, RxJS 7.8, TS 5.9). Nx 21 fijado por incompat. Nx 23
  ([TS-003](./TROUBLESHOOTING.md#ts-003--nx-23-incompatible-con-angular-ts-solution-setup)).
- ✅ **Fase 0**: `atalaya-web` (shell + 4 rutas lazy, OnPush), `simulator` (Node), CI.
- ✅ **Backend .NET 8** (`Atalaya.sln`): `libs/contracts`, `apps/api` (Minimal API + SignalR),
  `apps/worker`, `apps/api.tests`. **.NET SDK 8.0.422** ([TS-001](./TROUBLESHOOTING.md#ts-001--no-hay-net-sdk-solo-runtime)).
- ✅ **Infra Docker**: LocalStack 3.7 (SNS/SQS/S3) + Redis + Postgres ([AUD-005](./AUDIT.md)).
- ✅ **Fase 1 — pipeline REAL cableado** ([AUD-006](./AUDIT.md)): `/ingest`→SNS→SQS→worker→
  dedup(Redis)→Postgres(read model)→Redis pub/sub→API(`RedisDeltaForwarder`)→SignalR→dashboard.
  Verificado E2E (4.680 ev, 0 pérdida).
- ✅ **Frontend en vivo** ([AUD-004](./AUDIT.md)) + **zoneless** ([AUD-007](./AUDIT.md), ADR-010):
  `FleetStore` (firehose fuera de NgRx, coalescencia 100ms, signals), mapa canvas + tabla.
- ✅ **Revisión crítica** ([AUD-008](./AUDIT.md)) → **Fase 1.5 endurecimiento** ([AUD-009](./AUDIT.md)):
  reconexión sin huecos · latencia P95 en dashboard + **OTel** en worker · push endurecido
  (await + coalescencia server-side + backpressure) · **auth de ingesta** (token + rate limit) ·
  **carga k6** + consumidores SQS en paralelo.
- ✅ **Fase 2 — alertas** (inicial [AUD-011](./AUDIT.md), **rehecho como incidentes** [AUD-017](./AUDIT.md)):
  reglas con histéresis + máquina de estados → read model `alert_incidents` (una fila por `(device,rule)`)
  → Redis `atalaya:alerts:new` → API `RedisAlertForwarder`→SignalR (`alertsRaised`, `AlertIncident[]`)
  → feature `alerts` (estado abierta/resuelta, badge de críticas abiertas). Paridad InMemory. Verificado E2E.
- ✅ **Fase 2 — camino frío** ([AUD-012](./AUDIT.md)): tabla `telemetry` **particionada por tiempo**
  (`PostgresTelemetryArchive`, particiones diarias perezosas) + **S3 data lake** (`S3RawEventArchive`,
  clave por hash de contenido [AUD-016](./AUDIT.md)) → `GET /api/history` → feature `history`
  (selector + gráfico SVG + tabla). Paridad InMemory. Métrica `atalaya.telemetry.archived`.
- ✅ **Productivización**: **CDK** ([AUD-013](./AUDIT.md), `infra/cdk/`, synth + `cdklocal deploy`) +
  **grupos por viewport** SignalR ([AUD-014](./AUDIT.md), push O(viewport), opt-in sin regresión).
- ✅ **Fase 2.5 calidad de datos** ([AUD-016](./AUDIT.md) + [AUD-017](./AUDIT.md)): retención por
  **DROP PARTITION** (job en worker) + **S3 idempotente** + **alertas como incidentes** con histéresis.
- ✅ **Fase 3 endurecimiento operativo** ([AUD-018](./AUDIT.md)): **readiness** gateada (`/health/ready`
  API+worker, checks Postgres/Redis/SNS/SQS) + **graceful shutdown** (drena el buffer de ingesta) +
  **Testcontainers** (tests del SQL real, skippables sin Docker). Verificado E2E (ready→503 con Redis caído).
- ✅ **Fase 3 seguridad — auth de lecturas** ([AUD-019](./AUDIT.md)): **JWT Bearer** flag-gated
  (`Auth:Mode` Disabled/Dev/Oidc, OIDC-ready hacia Cognito) + **RBAC operador/admin** (read policy) en
  `/api/devices|alerts|history` y el hub (token por `?access_token=`). Emisor dev `/auth/dev-token` +
  dashboard con **auto-token silencioso** (interceptor `/api/*` + `accessTokenFactory`). La ingesta no
  cambia (token de dispositivo). 39/39 tests + E2E en vivo (401/200/400/403).
- ✅ **Pivote a GCP — G1/G2/G3/G4** (ADR-013, [AUD-020](./AUDIT.md)…[AUD-024](./AUDIT.md)): **Pub/Sub**
  (publisher+consumer tras flag `Telemetry:Transport=Gcp`, DLQ+veneno) · **Cloud Storage** data lake
  (`GcsRawEventArchive`, dedup check+commit, **NDJSON**) · **Identity Platform** auth OIDC real (login
  Firebase, RBAC por custom claim) · **BigQuery** external table sobre el lake + endpoint
  `/api/analytics/devices` (cost guard `MaximumBytesBilled`). Verificados E2E contra emuladores (G1/G2)
  y el proyecto real **fabian-portafolio** (G3 auth, G4 BigQuery). 43/43 tests.
- ✅ **Pivote a GCP — G5a IaC** (ADR-013, [AUD-025](./AUDIT.md)): **Terraform** (`infra/terraform/`) del
  plano de control GCP completo (Artifact Registry · SAs mínimas · Pub/Sub+DLQ+IAM · GCS · BigQuery ·
  Cloud SQL · Memorystore+VPC connector · Secret Manager · 2× Cloud Run always-on · Firebase Hosting),
  reemplaza el AWS CDK. **Dockerfiles** api/worker + container-readiness (worker health en `$PORT`, CORS
  por `Cors:Origins`). `terraform validate` ✅ + imágenes construyen ($0). **Siguiente: G5b** (apply real
  + E2E en la nube + teardown; ver §7).

### ✅ Hallazgo de carga de AUD-009 — RESUELTO ([AUD-010](./AUDIT.md))
El cuello era el **`PublishAsync` síncrono a SNS por request** (p95 de `/ingest` = 34 s bajo
concurrencia). Se **desacopló el publish** (remedio #2): `/ingest` encola en un canal acotado
(`QueueingTelemetryPublisher`) y responde 202; un `SnsBatchPublisher` (BackgroundService) drena,
coalesce (25 ms) y publica a SNS por **lotes** (`PublishBatch`, ≤10 msgs/llamada). **k6:** p95 de
`/ingest` baja a **34 ms** (≈1000×), 0% error, sosteniendo ~5.000 ev/s contra LocalStack; E2E con
cero pérdida (59.200/59.200). Deuda menor: reintento/persistencia ante `PublishBatch` fallido
(hoy se loguea y se pierde). A escala real: medir contra AWS y, si hace falta, ingesta serverless
(remedio #3, sin urgencia).

### Decisiones de implementación a recordar
- **Flag `Telemetry:Transport`**: `Aws` en Development (pipeline real, requiere Docker arriba);
  `InMemory` en base/tests (procesa en la API, sin Docker). Tests fuerzan InMemory.
- **Auth ingesta**: header `X-Ingest-Token`; en Development el token es `dev-ingest-token`
  (config `Ingest:Token`); el simulador lo manda con `--token`. Vacío en base = sin auth.
- API en **puerto 3000** (`apps/api/Properties/launchSettings.json`, perfil `http`).
- Worker: `Aws:Consumers` (consumidores SQS en paralelo). OTel exporta a consola en dev (10s).
- **Ingesta desacoplada (AUD-010)**: `/ingest` solo encola (202); el `SnsBatchPublisher` publica a
  SNS por lotes. Tunables en sección `Aws`: `PublisherQueueCapacity`, `MessageMaxEvents`, `FlushMilliseconds`.
- **Alertas como incidentes (AUD-017)**: `AlertRules.Read` emite `RuleReading` (Firing/Clear con
  histéresis); `IncidentTransitions.Decide` (puro) abre/escala/resuelve; store `IAlertIncidentStore`
  (`alert_incidents`, una fila por `(device,rule)`, Postgres+InMemory). Solo se notifican transiciones.
  Canal Redis `atalaya:alerts:new`; evento `alertsRaised` (lleva `AlertIncident[]`); `/api/alerts` =
  activos. Frontend `AlertStore` indexa por `incidentId`.
- **Camino frío (AUD-012)**: `ITelemetryArchive` (tabla `telemetry` particionada por día, retención
  O(1)) + `IRawEventArchive` (S3, solo en worker; `NullRawEventArchive` en InMemory). Endpoint
  `/api/history?deviceId&minutes&limit`. Worker config `Aws:Bucket` (data lake). S3 con `ForcePathStyle`.
- **Viewport (AUD-014)**: `ViewportRegistry` + hub `SyncViewport(ids)`/`ClearViewport()`; el forwarder
  hace envío dual (`Clients.All` por defecto; `AllExcept` + grupos `device:{id}` si hay clientes
  viewport). Dashboard: control Todo/2×/4×. Opt-in: sin clientes viewport, idéntico al firehose.
- **IaC (AUD-013)**: `infra/cdk/` (proyecto standalone). `npm run synth` / `deploy:local` (cdklocal).
  Requiere `aws-cdk-local` 3.x con la CLI nueva ([TS-007](./TROUBLESHOOTING.md)).
- **Fase 3 operativo (AUD-018)**: API `/health/live` + `/health/ready` (deps); worker health en
  `:3100` (config `Health:Port`). `SnsBatchPublisher` drena el buffer al apagar. Tests Postgres reales
  con Testcontainers (se **saltan** si no hay Docker). Retención: config `Retention:Days`/`IntervalHours`.
- **Auth de lecturas (AUD-019)**: flag `Auth:Mode` (`Disabled` base/tests · `Dev` HS256 local ·
  `Oidc` JWKS/Cognito). `AuthExtensions.AddAtalayaAuth` registra JWT Bearer + policies `read`
  (rol operador/admin) y `admin`; claim de rol `role`. Lecturas REST + hub usan `.RequireAuthorization
  ("read")` solo si `Auth:Enabled`; hub recibe el token por `?access_token=` (`OnMessageReceived`).
  `/auth/dev-token?role=` (solo modo Dev) mintea el JWT (`DevTokenIssuer`). Dev key en
  `appsettings.Development` (`Auth:DevSigningKey`). `/ingest` y `/health/*` NO se autentican.
  Frontend: `core/auth/AuthService` (auto-token al arrancar vía `provideAppInitializer`) +
  `authInterceptor` (Bearer en `/api/*`) + `accessTokenFactory` en el hub. Tests fuerzan `Auth:Mode=Disabled`.
- **Pub/Sub G1 (AUD-021, ADR-013)**: flag `Telemetry:Transport=Gcp` (junto a `Aws`/`InMemory`).
  API `GcpPubSubBatchPublisher` (espeja a SNS: canal→lotes; mensaje = array JSON de N eventos) +
  health `PubSubHealthCheck`. Worker `GcpPubSubConsumer` (SubscriberClient) → `TelemetryBatchProcessor`
  (lote común a SQS y Pub/Sub). Readiness `IWorkerReadiness` (`SqsReadiness`/`PubSubReadiness`).
  Config sección `Gcp` (`ProjectId`/`TopicId`/`SubscriptionId`/`EmulatorHost`). El cliente apunta al
  emulador por `PUBSUB_EMULATOR_HOST` (de `Gcp:EmulatorHost`); auto-crea topic+suscripción contra el
  emulador (idempotente, con reintento si aún levanta), en prod lo hará Terraform (G5). En modo Gcp el
  data lake crudo = `GcsRawEventArchive` (G2).
  **Resiliencia (paridad SQS):** suscripción con `DeadLetterPolicy` (DLQ `atalaya-telemetry-dlq`, 5
  intentos); handler distingue **veneno** (no deserializa → `Ack`+log+métrica `atalaya.events.poison`)
  de **transitorio** (→`Nack`, reintenta→DLQ). En GCP real la DLQ exige IAM al SA de Pub/Sub (Terraform, G5).
- **Data lake GCS G2 (AUD-022, ADR-013)**: `GcsRawEventArchive` (worker, modo Gcp) espeja al
  `S3RawEventArchive` y reusa `RawEventKey` (clave `raw/yyyy/MM/dd/{sha256}.json`). `StorageClient` con
  `BaseUri`+`UnauthenticatedAccess` explícitos contra **fake-gcs** (el cliente .NET no resuelve bien
  `STORAGE_EMULATOR_HOST` con fake-gcs); en GCP real `StorageClient.Create()` (ADC). Config `Gcp:Bucket`
  + `Gcp:StorageEmulatorHost`. Cloud SQL = swap de connection string (sin código, G5).
- **Dedup check+commit (revisión crítica G2, AUD-022)**: `IEventDeduplicator` es de dos pasos —
  `FilterNewAsync` solo consulta (`EXISTS`, no marca) y `CommitAsync` marca (`SET`) **tras** aplicar
  todos los efectos en `TelemetryBatchProcessor`. Antes el dedup marcaba al filtrar (`SET NX`), así que
  un fallo posterior (p.ej. subida a GCS) + redelivery filtraba el evento como duplicado → hueco
  permanente en el data lake. Seguro porque todos los efectos son idempotentes (guard `seq`, clave por
  hash, máquina de incidentes). Aplica a ambos brokers (SQS y Pub/Sub).
- **Auth OIDC real G3 (AUD-023, ADR-013)**: backend `Auth:ProjectId` deriva `EffectiveAuthority`
  (`securetoken.google.com/{projectId}`) y `EffectiveAudience` (projectId); valida tokens de Identity
  Platform contra el JWKS real; rol por custom claim `role`. Frontend `AuthService` con modo (`AUTH_CONFIG`):
  `dev` (token silencioso `/auth/dev-token`) · `firebase` (login real, firebase **dynamic-import** → fuera
  del bundle inicial y de Jest) · `disabled`. Componente `Login`; el `App` ata el stream al estado de
  sesión (start al autenticar, **stop+disconnect al cerrar sesión** — fix de revisión crítica). Custom
  claims con `scripts/set-role.mjs` (firebase-admin, acepta ruta de service account key, sin gcloud).
  Proyecto real **fabian-portafolio**; `apiKey` web es público. Default del dashboard = `dev`
  (`useFirebaseAuth` en `app.config.ts` lo cambia a firebase). Tests fuerzan auth desactivada/stub.
- **Analítica BigQuery G4 (AUD-024, ADR-013)**: **el lake ahora es NDJSON** (un evento por línea,
  `RawEventKey` — compartido con S3; la clave/hash idempotente no cambia). Setup reproducible
  `scripts/bigquery-setup.mjs` (`@google-cloud/bigquery`, sin gcloud) crea dataset `atalaya_analytics`
  + **external table** `telemetry_raw` sobre `gs://atalaya-datalake/raw/*.json` (`NEWLINE_DELIMITED_JSON`;
  ubicación del dataset = la del bucket, `BQ_LOCATION`). Endpoint `/api/analytics/devices?minutes&limit`
  (`BigQueryAnalyticsQuery` tras `IAnalyticsQuery`, RBAC `read`) → agregados por dispositivo. Se registra
  **solo si `Gcp:DatasetId`** (ausente en base/tests). Cliente con ADC (`GOOGLE_APPLICATION_CREDENTIALS`).
  **Cost guard** `Gcp:AnalyticsMaxBytesBilled` (default 1 GB, `MaximumBytesBilled`) porque el layout
  `yyyy/MM/dd` **no es hive** → sin poda de particiones, cada query escanea todo el lake (pay-per-byte).
  IAM runtime mínimo de la SA de consulta: `bigquery.jobUser` + `bigquery.dataViewer` + `storage.objectViewer`
  (sobre el bucket). El setup (crear dataset/tabla) exige editor (`bigquery.dataEditor`/admin), no solo viewer.
- **IaC Terraform G5a (AUD-025, ADR-013)**: `infra/terraform/` (reemplaza `infra/cdk/`). Un archivo por
  dominio (apis/artifact_registry/service_accounts/pubsub/storage/bigquery/sql/redis/network/secrets/
  cloudrun/firebase/outputs). Provider `google`/`google-beta` ~> 6.0; **backend GCS** parametrizado
  (`-backend-config="bucket=..."`). Las apps en Cloud Run usan la **identidad del servicio** (ADC, sin
  keys); SAs `atalaya-api`/`atalaya-worker` de mínimo privilegio. Cloud Run → **Cloud SQL** por socket
  unix (`/cloudsql/...`, connection string en Secret Manager) y → **Memorystore** por **VPC connector**
  (`egress=PRIVATE_RANGES_ONLY`). Ambos servicios **always-on** (`min_instances=1`, `cpu_idle=false`);
  worker `max_instances=1` (caché de incidentes, AUD-017). El Terraform **cierra el gap de IAM de la DLQ**
  de Pub/Sub (G1). **Cambios de app para contenedor:** worker health en `http://+:$PORT` cuando hay `PORT`
  (Cloud Run); CORS por `Cors:Origins`. Dockerfiles multi-stage en `apps/{api,worker}/Dockerfile` (contexto
  = raíz). **Validado `terraform validate` + build de imágenes ($0)**; `apply`/`firebase deploy`/teardown = G5b.
- Interfaces de extensión (para swaps sin reescribir): `ITelemetryPublisher`, `IDeviceStateRepository`,
  `IAlertIncidentStore`, `ITelemetryArchive`, `IRawEventArchive`, `IEventDeduplicator`,
  `ITelemetryBroadcaster`, `IAlertBroadcaster`, `IAnalyticsQuery`.
- `nuget.config` en la raíz ([TS-004](./TROUBLESHOOTING.md#ts-004--dotnet-no-resuelve-paquetes-nuget-sin-fuentes)).

### Cómo levantar todo (pipeline real)
```
docker compose -f infra/docker-compose.yml up -d     # infra (healthy)
npx nx serve api        # API :3000 (modo Aws)
npx nx serve worker     # consumidores SQS
npm start               # dashboard :4200
npx nx build simulator
node dist/apps/simulator/main.js --rate 2000 --devices 100 --duration 30 --url http://localhost:3000/ingest --token dev-ingest-token
```
**Modo GCP (Pub/Sub + GCS, AUD-021/022)** — emuladores en vez de LocalStack, costo $0:
```
docker compose -f infra/docker-compose.yml up -d redis postgres pubsub-emulator fake-gcs   # perfil gcp
$env:Telemetry__Transport="Gcp"; npx nx serve api      # publica al emulador Pub/Sub
$env:Telemetry__Transport="Gcp"; npx nx serve worker   # consume Pub/Sub + archiva en GCS (fake-gcs)
```
Verificación: `npx nx run-many -t build lint test` · `nx test api-tests` (**43/43** con Docker;
**40 + 3 saltados** sin Docker, por los tests de Testcontainers). Health: `curl :3000/health/ready`
y `:3100/health/ready` (worker). Carga: ver [DEPLOY.md §1.6](./DEPLOY.md) (k6 vía Docker).
Analítica (G4, BigQuery real): `node scripts/bigquery-setup.mjs fabian-portafolio atalaya-datalake
atalaya_analytics <sa-key.json>` y, con la API en modo Gcp + `Gcp__DatasetId=atalaya_analytics` +
`GOOGLE_APPLICATION_CREDENTIALS`, `curl ":3000/api/analytics/devices?minutes=60&limit=10"`.
Auth (modo Dev en Development): `curl :3000/auth/dev-token?role=operador` → JWT; el dashboard lo
adquiere solo. Lecturas sin token → 401; con rol operador/admin → 200.

### Pendiente (próxima sesión)
**No quedan fases de features** — el producto (mapa, dispositivos, alertas-incidentes, históricos)
está completo y verificado E2E. Lo que resta es **endurecimiento incremental** y **solo-AWS-real**:

- ✅ **Mapa real (deck.gl) + virtual scroll (CDK)** hechos ([AUD-026](./AUDIT.md)): dashboard con basemap
  OSM + `ScatterplotLayer` geolocalizado (deck.gl dynamic-import, fuera del bundle inicial); tabla de
  dispositivos con `cdk-virtual-scroll-viewport` (sin cap). Verificado E2E en navegador real.
- **Fase 3 — resto** (SAD §10, local, sin urgencia): DLQ **replay** · downsampling del histórico ·
  runbooks · login real/refresh-token (auth: hoy auto-token dev).
- **Solo AWS real** (requiere cuenta, hoy bloqueado): **Athena** sobre el data lake S3 · medir
  throughput contra AWS · CDK multi-entorno (dev/staging/prod).
- **Deuda menor anotada**: durabilidad del borde de ingesta = best-effort (drena al apagar, pero
  el 202 es previo a durabilidad → real: API GW→SNS, AUD-015 B) · reintento ante `PublishBatch`
  fallido (AUD-010) · caché de incidentes abiertos coherente solo en 1 worker (FIFO si hay varias
  instancias, AUD-017) · el simulador no genera valores críticos (lo crítico solo por unit test).

### Toolchain verificado (2026-06-21)
- ✅ git 2.51 · Node v24.15 · npm 11.14 · Nx 21.6.11 · **.NET SDK 8.0.422** · **Docker 29.5.3**
- ⚠️ **Almacén de Docker movido a `D:\DockerData`** (C: se había llenado a 0 bytes, TS-006).
  Hay un junction en `%LOCALAPPDATA%\Docker\wsl\disk` → `D:\DockerData\disk`. No lo borres.
- ⚠️ Disco **C: muy justo**: vigilar espacio antes de pulls grandes.
- ⚠️ **Docker no está en PATH**; el binario está en `C:\Program Files\Docker\Docker\resources\bin`.
  Docker Desktop a veces hay que **arrancarlo** (`Docker Desktop.exe`); los tests de Testcontainers
  y el pipeline real lo necesitan. `nx serve` deja procesos `dotnet`/`Atalaya.*` que **bloquean DLLs**
  al recompilar: mátalos (`Stop-Process -Name dotnet,Atalaya.Api,Atalaya.Worker -Force`) si un build falla por lock.

### Estructura actual del repo
```
atalaya/
├─ apps/
│  ├─ atalaya-web/   # SPA Angular (shell + features lazy)
│  ├─ simulator/     # generador de carga de telemetría (Node)
│  ├─ api/           # .NET Minimal API + SignalR + ingesta(SNS) + forwarders Redis→SignalR + health + auth(JWT/RBAC)
│  ├─ worker/        # .NET Worker: SQS → dedup → read models + incidentes + camino frío + retención + health; OTel
│  └─ api.tests/     # xUnit: InMemory (caliente/incidentes/histórico/viewport/auth) + puros + Testcontainers (Postgres real)
├─ libs/contracts/   # DTOs (TelemetryEvent, DeviceState, AlertIncident, AlertRules, IncidentTransitions)
├─ libs/persistence/ # Postgres: device_state + alert_incidents + telemetry particionada + retención + IRawEventArchive
├─ libs/realtime/    # Redis: dedup (ADR-006) + broadcasters pub/sub deltas/alertas (ADR-002)
├─ infra/            # docker-compose (LocalStack+Redis+Postgres + pubsub-emulator/fake-gcs perfil gcp) + cdk/ (AWS) + terraform/ (GCP, G5) + load/ingest.js (k6)
├─ Atalaya.sln, nuget.config
├─ *.md              # SAD, README, AUDIT, DEPLOY, TROUBLESHOOTING, CLAUDE
└─ nx.json, package.json, tsconfig.base.json, eslint.config.mjs
```

## 6. Decisiones de arquitectura clave (resumen — el detalle está en el SAD)

- **ADR-001:** Ingesta desacoplada SNS→SQS; nunca escritura directa a DB.
- **ADR-002:** SignalR (WebSocket) + backplane Redis para push en vivo.
- **ADR-003:** El **firehose en vivo NO entra al NgRx Store global** — va por capa RxJS +
  Component Store. El Store es para estado de app/agregados.
- **ADR-004:** RxJS disciplinado (`async pipe` + `takeUntilDestroyed`, prohibido subscribe
  anidado).
- **ADR-005:** Camino caliente (read models) vs frío (SQL particionado + S3) — CQRS pragmático.
- **ADR-006:** At-least-once + idempotencia (dedup Redis + constraint SQL) + replay de gaps.
- **ADR-007:** SQL particionado por tiempo + S3 data lake (retención O(1) por drop de partición).
- **ADR-008:** .NET Minimal API + Worker Services separados (escalan distinto).
- **ADR-009:** IaC con AWS CDK + LocalStack en dev.
- **ADR-010:** Frontend de alto rendimiento: OnPush + Signals + coalescencia (zoneless, hecho).
- **ADR-011:** Implementación incremental con shims tras interfaces (flag `Telemetry:Transport`);
  puente Redis pub/sub e `awslocal` como interinos del backplane SignalR y CDK.
- **ADR-012:** Auth de lecturas JWT Bearer flag-gated (`Auth:Mode` Disabled/Dev/Oidc) + RBAC operador/admin.
- **ADR-013:** **Pivote de nube AWS → GCP** (mismo diseño, otro proveedor; adaptadores GCP tras las
  interfaces; emuladores locales para no gastar). Mapeo de servicios en el SAD; roadmap en AUD-020.

## 7. Próximos pasos sugeridos (para la nueva sesión)

**Estado:** Fases 0→3 completas en AWS/LocalStack + **pivote a GCP G1/G2/G3/G4 verificados E2E** +
**G5a (IaC Terraform) escrito y validado** ([AUD-021](./AUDIT.md)…[AUD-025](./AUDIT.md)). Último commit:
**G5a** (`11b8b9c`). **G5b (apply real con costo) se DEJA PARA EL FINAL por decisión** — primero el
backlog sin costo (features/endurecimiento); el despliegue vivo + medición + teardown se hacen al cierre.
Para retomar,
leer §1 (banner de pivote) + §5 + [AUD-020](./AUDIT.md) (roadmap) + los últimos audits
[AUD-024](./AUDIT.md)/[AUD-025](./AUDIT.md) + `infra/terraform/README.md` (runbook de G5b).

> **Flujo por fase G (acordado, [memoria] `gcp-phase-workflow`):** implementar → **verificar E2E real**
> (no solo build/tests) → **revisión crítica** → aplicar el fix que surja → documentar en los .md →
> **commit + push** a `origin main`. La revisión crítica va **antes** del push. En G1 halló el gap de
> DLQ; en G2 la pérdida por dedup-antes-de-efectos; en G3 el WebSocket vivo tras logout; en G4 la
> falta de tope de costo (BigQuery pay-per-byte sin poda de particiones → cost guard).

**Para correr el pipeline GCP (emuladores, costo $0):**
```
docker compose -f infra/docker-compose.yml up -d redis postgres pubsub-emulator fake-gcs
$env:Telemetry__Transport="Gcp"; npx nx serve api        # + worker igual
```
Auth Oidc real (G3): API con `Auth__Mode=Oidc` + `Auth__ProjectId=fabian-portafolio` valida tokens de
Identity Platform; dashboard en modo firebase = `useFirebaseAuth=true` en `app.config.ts`.

**Roadmap del pivote a GCP (orden de ejecución, cada fase verificable):**
0. **G0 — Fundaciones**: proyecto GCP + **Budget+Alert** (tope) · habilitar APIs · service accounts.
   Hecho: proyecto **fabian-portafolio** existe (Identity Platform activo en G3, BigQuery API habilitada
   + budget alert en G4). ✋ Pasos de consola los hace el usuario.
1. **G1 — Pub/Sub** ✅ **HECHO** ([AUD-021](./AUDIT.md)): `GcpPubSubBatchPublisher` (API) +
   `GcpPubSubConsumer` (worker) tras el flag `Telemetry:Transport=Gcp`, lote común en
   `TelemetryBatchProcessor`, readiness `IWorkerReadiness`. Verificado E2E contra el emulador.
2. **G2 — GCS + camino frío** ✅ **HECHO** ([AUD-022](./AUDIT.md)): `GcsRawEventArchive` (reusa
   `RawEventKey`) contra fake-gcs; `StorageClient` con `BaseUri`+`UnauthenticatedAccess` en emulador.
   Verificado E2E. Cloud SQL = connection string (sin código, G5).
3. **G3 — Auth Identity Platform** ✅ **HECHO** ([AUD-023](./AUDIT.md)): `Auth:ProjectId` deriva el
   authority/audience de Identity Platform; login real Angular (Firebase Auth, dynamic-import);
   roles por custom claim (`scripts/set-role.mjs`). Verificado E2E contra `fabian-portafolio`
   (401/403/200). Proyecto real: **fabian-portafolio**; test user `atalaya-test@atalaya.dev`.
4. **G4 — BigQuery** ✅ **HECHO** ([AUD-024](./AUDIT.md)): se eligió **external table** (Opción A) sobre
   `gs://atalaya-datalake/raw/*.json`. El lake pasó a **NDJSON** (un evento por línea en `RawEventKey`,
   resolviendo el ⚠️ del array). Setup `scripts/bigquery-setup.mjs` (dataset `atalaya_analytics` + tabla
   `telemetry_raw`). Endpoint **`/api/analytics/devices`** (`BigQueryAnalyticsQuery` tras `IAnalyticsQuery`)
   con **cost guard** `MaximumBytesBilled`. Verificado E2E contra BigQuery real (`fabian-portafolio`).
   IAM runtime mínimo de la SA `bq-query-service`: `bigquery.jobUser` + `bigquery.dataViewer` +
   `storage.objectViewer`. **No hay emulador de BigQuery** → se validó contra el proyecto real.
5. **G5 — IaC Terraform + despliegue Cloud Run** (API+worker) + SPA a Firebase Hosting.
   - **G5a** ✅ **HECHO** ([AUD-025](./AUDIT.md)): Terraform completo en `infra/terraform/` (reemplaza el
     CDK) + Dockerfiles api/worker + container-readiness (worker health en `$PORT`, CORS por `Cors:Origins`).
     `terraform validate` ✅ + imágenes que construyen ($0). Worker = **pull + min-instances=1** (decisión).
     Cierra el gap de IAM de la DLQ de Pub/Sub (G1).
   - **G5b** (DIFERIDO AL FINAL por decisión, 2026-06-24): `terraform apply` real (crear bucket de tfstate → init con backend → publicar
     imágenes en Artifact Registry → apply) + **`firebase deploy`** de la SPA + **smoke E2E en la nube**
     + **teardown**. Runbook en `infra/terraform/README.md`. ⚠️ Cobra (~US$70–120/mes encendido):
     ventana acotada + Budget+Alert + `terraform destroy` al terminar.
6. **G6 — Medición real** (k6 contra Pub/Sub real) + **script de teardown** (apagar Cloud SQL/Memorystore).

⚠️ **Costo**: BigQuery pay-per-byte (free-tier cubre dev); Cloud SQL/Memorystore cobran ociosos →
Budget+Alert + teardown obligatorios.
Backlog AWS-era que aplica igual en GCP (de [AUD-015](./AUDIT.md)): DLQ replay, downsampling,
login real/refresh-token. (Mapa real deck.gl + virtual scroll ✅ hechos, [AUD-026](./AUDIT.md).)

> Al cerrar cada sesión: actualiza §5 (estado), añade entrada en AUDIT.md si hubo cambio
> auditable, y registra en TROUBLESHOOTING.md cualquier error resuelto.
