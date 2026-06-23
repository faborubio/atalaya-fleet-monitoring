# CLAUDE.md — Contexto del proyecto Atalaya

> Este archivo es el **ancla de contexto** entre sesiones. Léelo al empezar cualquier
> sesión nueva para retomar sin perder el hilo. Manténlo actualizado: cuando cambie el
> estado real del proyecto, actualiza aquí.

---

## 1. Qué es Atalaya

Plataforma de **monitoreo y alertamiento de flota/IoT en tiempo real**. Ingiere telemetría
de miles de dispositivos, mantiene proyecciones en vivo y dispara alertas en sub-segundos.
Es **full-stack con foco frontend** (Angular): el backend existe para que el frontend en
tiempo real tenga carga real que estresar. Proyecto de portafolio orientado a una vacante
de Angular/.NET/AWS event-driven.

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
| [SAD-Atalaya.md](./SAD-Atalaya.md) | Arquitectura rectora (ADR-001…011, NFRs, roadmap) |
| [README.md](./README.md) | Visión, stack, estructura, cómo empezar |
| [AUDIT.md](./AUDIT.md) | Bitácora de auditorías por fase/cambio |
| [DEPLOY.md](./DEPLOY.md) | Despliegue local (LocalStack) y AWS (CDK) |
| [TROUBLESHOOTING.md](./TROUBLESHOOTING.md) | Errores encontrados y soluciones |
| **CLAUDE.md** | Este archivo: contexto entre sesiones |

## 5. Estado actual

**Fecha de actualización:** 2026-06-22
**Fase:** 1 + 1.5 + ingesta desacoplada ([AUD-010](./AUDIT.md)) + **Fase 2 completa** (alertas
[AUD-011](./AUDIT.md) + camino frío [AUD-012](./AUDIT.md)) + **productivización**: CDK
([AUD-013](./AUDIT.md)) + viewport ([AUD-014](./AUDIT.md)) + **Fase 2.5 calidad de datos**
([AUD-016](./AUDIT.md), [AUD-017](./AUDIT.md)). Pendiente real: Athena (cuenta AWS).

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
- ✅ **Fase 2 — alertas por umbral** ([AUD-011](./AUDIT.md)): motor de reglas puro (`AlertRules`
  en contracts) → read model `alerts` (Postgres, insert idempotente) → worker publica a canal
  Redis `atalaya:alerts:new` → API `RedisAlertForwarder`→SignalR (`alertsRaised`) → feature
  `alerts` (tabla en vivo + conteos, badge de críticas en nav). Paridad InMemory para tests.
  Verificado E2E. Métrica `atalaya.alerts.raised`.
- ✅ **Fase 2 — camino frío** ([AUD-012](./AUDIT.md)): tabla `telemetry` **particionada por tiempo**
  (`PostgresTelemetryArchive`, particiones diarias perezosas, retención O(1)) + **S3 data lake**
  (`S3RawEventArchive` en worker, `raw/yyyy/MM/dd/`) → `GET /api/history` → feature `history`
  (selector + gráfico SVG + tabla). Paridad InMemory. Verificado E2E (4.950 filas, 10 objetos S3).
  Métrica `atalaya.telemetry.archived`. Athena = solo AWS real (deuda documentada).

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
- Interfaces de extensión (para swaps sin reescribir): `ITelemetryPublisher`, `IDeviceStateRepository`,
  `IAlertRepository`, `ITelemetryArchive`, `IRawEventArchive`, `IEventDeduplicator`,
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
Verificación: `npx nx run-many -t build` · `nx test api-tests` · `npx nx run-many -t lint test`.
Carga: ver [DEPLOY.md §1.6](./DEPLOY.md) (k6 vía Docker).

### Pendiente (próxima sesión)
- ~~Resolver el cuello de botella de ingesta~~ ✅ HECHO ([AUD-010](./AUDIT.md): desacople + batch a SNS).
  Deuda residual menor: reintento/persistencia ante `PublishBatch` fallido (hoy se loguea y se pierde).
- ~~Fase 2 — alertas por umbral + read model de alertas~~ ✅ HECHO ([AUD-011](./AUDIT.md)).
- ~~Fase 2 — camino frío (telemetry particionada + S3 data lake + vista histórica)~~ ✅ HECHO ([AUD-012](./AUDIT.md)).
- ~~Productivizar: infra con **CDK** (ADR-009)~~ ✅ HECHO ([AUD-013](./AUDIT.md)): `infra/cdk/`
  (`cdk synth` offline + `cdklocal deploy` verificado). El `01-resources.sh` queda como atajo de dev.
- ~~Grupos por viewport en SignalR (AUD-008)~~ ✅ HECHO ([AUD-014](./AUDIT.md)): push O(viewport),
  opt-in sin regresión (firehose por defecto); control Todo/2×/4× en el dashboard.
- **Productivizar (resto)**: **Athena** sobre el data lake S3 (solo AWS real, pendiente de cuenta).
- ~~**Fase 2.5 calidad de datos** (Top de [AUD-015](./AUDIT.md))~~ ✅ HECHO: retención por DROP
  PARTITION + S3 idempotente ([AUD-016](./AUDIT.md)) + alertas como **incidentes** con histéresis
  ([AUD-017](./AUDIT.md)). Verificado E2E.
- ~~**Fase 3 endurecimiento operativo**: readiness + graceful shutdown + Testcontainers~~ ✅ HECHO
  ([AUD-018](./AUDIT.md)). `/health/live` + `/health/ready` (API y worker); drenado del buffer al
  apagar; tests Postgres reales (skippables sin Docker). Verificado E2E (ready→503 con Redis caído).
- **Revisión crítica [AUD-015](./AUDIT.md)** — restante: durabilidad del borde de ingesta (API GW→SNS),
  auth de lecturas (OIDC/JWT), DLQ replay, downsampling del histórico, virtual scroll/mapa. Incremental.
- Deuda menor: reintento/persistencia ante `PublishBatch` fallido (AUD-010); el simulador no genera
  valores de alerta crítica (solo aviso), lo crítico solo se ve por unit test (AUD-011).

### Toolchain verificado (2026-06-21)
- ✅ git 2.51 · Node v24.15 · npm 11.14 · Nx 21.6.11 · **.NET SDK 8.0.422** · **Docker 29.5.3**
- ⚠️ **Almacén de Docker movido a `D:\DockerData`** (C: se había llenado a 0 bytes, TS-006).
  Hay un junction en `%LOCALAPPDATA%\Docker\wsl\disk` → `D:\DockerData\disk`. No lo borres.
- ⚠️ Disco **C: muy justo**: vigilar espacio antes de pulls grandes.

### Estructura actual del repo
```
atalaya/
├─ apps/
│  ├─ atalaya-web/   # SPA Angular (shell + features lazy)
│  ├─ simulator/     # generador de carga de telemetría (Node)
│  ├─ api/           # .NET Minimal API + SignalR + ingesta(SNS) + forwarders Redis→SignalR (deltas/alertas) + auth
│  ├─ worker/        # .NET Worker: SQS → dedup → read models + alertas + camino frío (telemetry+S3); OTel
│  └─ api.tests/     # xUnit: caliente + alertas + histórico (InMemory) + reglas + batching SNS
├─ libs/contracts/   # DTOs .NET compartidos (TelemetryEvent, DeviceState, Alert, AlertRules)
├─ libs/persistence/ # Postgres: device_state + alerts + telemetry particionada + IRawEventArchive (Dapper/Npgsql)
├─ libs/realtime/    # Redis: dedup (ADR-006) + broadcaster pub/sub (ADR-002)
├─ infra/            # docker-compose (LocalStack+Redis+Postgres) + load/ingest.js (k6)
├─ Atalaya.sln, nuget.config
├─ *.md              # SAD, README, AUDIT, DEPLOY, TROUBLESHOOTING, CLAUDE
└─ nx.json, package.json, tsconfig.base.json, eslint.config.mjs
```
Pendiente de crear: infra como **AWS CDK** (ADR-009). (S3 data lake + tabla `telemetry` ✅ AUD-012.)

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

## 7. Próximos pasos sugeridos (para la nueva sesión)

1. **Leer este archivo + [AUD-011](./AUDIT.md) y [AUD-012](./AUDIT.md)** (Fase 2: alertas + camino frío).
2. ~~Desacoplar el publish a SNS~~ ✅ ([AUD-010](./AUDIT.md)). ~~Fase 2 alertas~~ ✅ ([AUD-011](./AUDIT.md)).
   ~~Fase 2 camino frío~~ ✅ ([AUD-012](./AUDIT.md)).
3. ~~Productivizar: **CDK** (ADR-009)~~ ✅ ([AUD-013](./AUDIT.md)). ~~Grupos por viewport~~ ✅ ([AUD-014](./AUDIT.md)).
4. Pendiente real: **Athena** sobre el data lake S3 (requiere cuenta AWS); medir throughput contra AWS real.

> Al cerrar cada sesión: actualiza §5 (estado), añade entrada en AUDIT.md si hubo cambio
> auditable, y registra en TROUBLESHOOTING.md cualquier error resuelto.
