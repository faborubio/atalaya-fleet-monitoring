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

**Fecha de actualización:** 2026-06-23
**Fase:** 1 + 1.5 + ingesta desacoplada ([AUD-010](./AUDIT.md)) + **Fase 2 completa** (alertas
[AUD-011](./AUDIT.md) + camino frío [AUD-012](./AUDIT.md)) + **productivización** (CDK
[AUD-013](./AUDIT.md) + viewport [AUD-014](./AUDIT.md)) + **Fase 2.5 calidad de datos**
([AUD-016](./AUDIT.md) + [AUD-017](./AUDIT.md)) + **Fase 3 endurecimiento operativo**
([AUD-018](./AUDIT.md)) + **Fase 3 seguridad: auth de lecturas OIDC/JWT** ([AUD-019](./AUDIT.md)).
**El producto (en AWS/LocalStack) está completo.** **Pivote a GCP en marcha** ([AUD-020](./AUDIT.md),
roadmap G0…G6): **G1 (Pub/Sub)** ([AUD-021](./AUDIT.md)) y **G2 (data lake en Cloud Storage)**
([AUD-022](./AUDIT.md)) hechos y verificados E2E contra emuladores. Lo siguiente: G3 (Identity Platform).

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
- Interfaces de extensión (para swaps sin reescribir): `ITelemetryPublisher`, `IDeviceStateRepository`,
  `IAlertIncidentStore`, `ITelemetryArchive`, `IRawEventArchive`, `IEventDeduplicator`,
  `ITelemetryBroadcaster`, `IAlertBroadcaster`.
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
Verificación: `npx nx run-many -t build lint test` · `nx test api-tests` (**42/42** con Docker;
**39 + 3 saltados** sin Docker, por los tests de Testcontainers). Health: `curl :3000/health/ready`
y `:3100/health/ready` (worker). Carga: ver [DEPLOY.md §1.6](./DEPLOY.md) (k6 vía Docker).
Auth (modo Dev en Development): `curl :3000/auth/dev-token?role=operador` → JWT; el dashboard lo
adquiere solo. Lecturas sin token → 401; con rol operador/admin → 200.

### Pendiente (próxima sesión)
**No quedan fases de features** — el producto (mapa, dispositivos, alertas-incidentes, históricos)
está completo y verificado E2E. Lo que resta es **endurecimiento incremental** y **solo-AWS-real**:

- **Fase 3 — resto** (SAD §10, local, sin urgencia): DLQ **replay** · downsampling del histórico ·
  virtual scroll + mapa real (deck.gl) · runbooks · login real/refresh-token (auth: hoy auto-token dev).
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
├─ infra/            # docker-compose (LocalStack+Redis+Postgres) + cdk/ (AWS CDK, ADR-009) + load/ingest.js (k6)
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

**Estado:** Fases 0→3 completas en **AWS/LocalStack** (ver §5). **Decisión activa: pivote a GCP**
([ADR-013](./SAD-Atalaya.md) / [AUD-020](./AUDIT.md)) — decidido y documentado, **sin implementar**.
Para retomar, leer §1 (banner de pivote) + §5 + [AUD-020](./AUDIT.md) (roadmap G0…G6) + ADR-013.

**Roadmap del pivote a GCP (orden de ejecución, cada fase verificable):**
0. **G0 — Fundaciones**: crear proyecto GCP + **Budget+Alert** (tope, protege los ~$200) · habilitar
   APIs · service accounts. ✋ Necesita al usuario. Docs ya hechas.
1. **G1 — Pub/Sub** ✅ **HECHO** ([AUD-021](./AUDIT.md)): `GcpPubSubBatchPublisher` (API) +
   `GcpPubSubConsumer` (worker) tras el flag `Telemetry:Transport=Gcp`, lote común en
   `TelemetryBatchProcessor`, readiness `IWorkerReadiness`. Verificado E2E contra el emulador.
2. **G2 — GCS + camino frío** ✅ **HECHO** ([AUD-022](./AUDIT.md)): `GcsRawEventArchive` (reusa
   `RawEventKey`) contra fake-gcs; `StorageClient` con `BaseUri`+`UnauthenticatedAccess` en emulador.
   Verificado E2E. Cloud SQL = connection string (sin código, G5).
3. **G3 — Auth Identity Platform**: `Auth:Mode=Oidc` real + login Angular (Firebase Auth) + roles por claims.
4. **G4 — BigQuery**: data lake GCS → BigQuery (cierra Athena).
5. **G5 — IaC Terraform + despliegue Cloud Run** (API+worker) + SPA a Firebase Hosting.
6. **G6 — Medición real** (k6 contra Pub/Sub real) + **script de teardown** (apagar Cloud SQL/Memorystore).

⚠️ **Costo**: Cloud SQL/Memorystore cobran ociosos → Budget+Alert + teardown obligatorios.
Backlog AWS-era que aplica igual en GCP (de [AUD-015](./AUDIT.md)): DLQ replay, downsampling, virtual
scroll + mapa real (deck.gl), login real/refresh-token.

> Al cerrar cada sesión: actualiza §5 (estado), añade entrada en AUDIT.md si hubo cambio
> auditable, y registra en TROUBLESHOOTING.md cualquier error resuelto.
