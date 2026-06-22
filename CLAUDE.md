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
**Fase:** 1 + 1.5 completas (camino caliente sobre infra real + endurecimiento). Próximo: Fase 2.

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

### 🔴 Hallazgo abierto más importante (AUD-009, prueba de carga)
Contra **LocalStack** el `/ingest` topa ~**1.000–1.300 ev/s** (NFR pide 5.000): bajo concurrencia
la latencia explota (p95 34s). **La cola SQS queda en 0 y el worker drena** → el cuello es el
**`PublishAsync` síncrono a SNS por request**, no el consumo. Remedios (orden de impacto):
1) desacoplar el publish (`/ingest` encola y responde 202; publicador en background hace *batch*
a SNS); 2) ingesta serverless (API GW+Lambda / Kinesis); 3) medir contra AWS real (LocalStack
no representa throughput).

### Decisiones de implementación a recordar
- **Flag `Telemetry:Transport`**: `Aws` en Development (pipeline real, requiere Docker arriba);
  `InMemory` en base/tests (procesa en la API, sin Docker). Tests fuerzan InMemory.
- **Auth ingesta**: header `X-Ingest-Token`; en Development el token es `dev-ingest-token`
  (config `Ingest:Token`); el simulador lo manda con `--token`. Vacío en base = sin auth.
- API en **puerto 3000** (`apps/api/Properties/launchSettings.json`, perfil `http`).
- Worker: `Aws:Consumers` (consumidores SQS en paralelo). OTel exporta a consola en dev (10s).
- Interfaces de extensión (para swaps sin reescribir): `ITelemetryPublisher`,
  `IDeviceStateRepository`, `IEventDeduplicator`, `ITelemetryBroadcaster`.
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
- **Resolver el cuello de botella de ingesta** (remedio #1: desacoplar publish a SNS con batch). ← alto valor.
- **Fase 2**: alertas por umbral + read model de alertas; **S3 data lake** + tabla `telemetry`
  particionada (ADR-007); vistas históricas (camino frío).
- Productivizar: infra con **CDK** (ADR-009); backplane nativo de SignalR + grupos por viewport
  (documentado en AUD-008 como opción, sin urgencia para flota completa).

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
│  ├─ api/           # .NET Minimal API + SignalR + ingesta(SNS) + forwarder Redis→SignalR + auth
│  ├─ worker/        # .NET Worker: consume SQS (N consumidores) → dedup → Postgres → publica deltas; OTel
│  └─ api.tests/     # xUnit, test de integración del camino caliente (InMemory)
├─ libs/contracts/   # DTOs .NET compartidos (TelemetryEvent, DeviceState)
├─ libs/persistence/ # read model en Postgres (Dapper/Npgsql)
├─ libs/realtime/    # Redis: dedup (ADR-006) + broadcaster pub/sub (ADR-002)
├─ infra/            # docker-compose (LocalStack+Redis+Postgres) + load/ingest.js (k6)
├─ Atalaya.sln, nuget.config
├─ *.md              # SAD, README, AUDIT, DEPLOY, TROUBLESHOOTING, CLAUDE
└─ nx.json, package.json, tsconfig.base.json, eslint.config.mjs
```
Pendiente de crear: infra como **AWS CDK** (ADR-009); S3 data lake + tabla `telemetry` (Fase 2).

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

1. **Leer este archivo + [AUD-008](./AUDIT.md) y [AUD-009](./AUDIT.md)** (backlog crítico y hallazgo de carga).
2. **Desacoplar el publish a SNS** en `/ingest` (encolar + batch en background) para subir el
   throughput de ingesta — es el cuello de botella medido.
3. **Fase 2**: alertas por umbral + S3 data lake + telemetría particionada (ADR-007).
4. Si se retoma la infra: pasar `awslocal` → **AWS CDK** (ADR-009).

> Al cerrar cada sesión: actualiza §5 (estado), añade entrada en AUDIT.md si hubo cambio
> auditable, y registra en TROUBLESHOOTING.md cualquier error resuelto.
