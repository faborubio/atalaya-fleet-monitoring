# Atalaya

> **Plataforma de monitoreo y alertamiento de flota/IoT en tiempo real.**
> Angular + .NET + **GCP**, arquitectura orientada a eventos, con despliegue real.

Atalaya es la torre de vigía: ingiere telemetría de miles de dispositivos, mantiene
proyecciones en vivo y dispara alertas en sub-segundos. Es un proyecto **full-stack con
foco frontend**: el backend existe para que el frontend en tiempo real tenga algo real
que estresar.

| | |
|---|---|
| **Versión** | 0.3.0 |
| **Estado** | 🟢 Fases 0→3 completas (en AWS/LocalStack) — verificado E2E · 🔵 **pivote a GCP** decidido, en migración ([ADR-013](./SAD-Atalaya.md)) |
| **Autor** | Fabián Rubio — Full Stack (foco Frontend / Angular) |
| **Repositorio** | https://github.com/faborubio/atalaya-fleet-monitoring |
| **Documento rector** | [SAD-Atalaya.md](./SAD-Atalaya.md) |

> **⚠️ Pivote de nube AWS → GCP** ([ADR-013](./SAD-Atalaya.md) / [AUD-020](./AUDIT.md)). El sistema
> está construido y verificado E2E sobre **AWS/LocalStack** (lo de abajo); la dirección activa es
> desplegarlo **de verdad en Google Cloud** (Pub/Sub, Cloud Storage, BigQuery, Identity Platform,
> Cloud Run, Cloud SQL, Memorystore), desarrollando con emuladores locales. Mismo diseño, otro
> proveedor — los adaptadores GCP entran tras las interfaces ya existentes. **Migración en curso, por
> fases G0…G6**; nada migrado aún.

> **Lo que funciona (verificado E2E, reproducible en local con Docker):**
> - **Camino caliente:** `simulador → API → SNS → SQS → worker → dedup(Redis) → Postgres → Redis → SignalR → dashboard en vivo`. Ingesta desacoplada (p95 `/ingest` ~34 ms, ~5.000 ev/s sin pérdida).
> - **Alertas como incidentes** con histéresis (abrir/escalar/resolver, no por-evento) y push en vivo.
> - **Camino frío:** telemetría particionada por tiempo (retención por `DROP PARTITION`) + data lake S3 idempotente + vista histórica.
> - **Productivización:** infra como **AWS CDK** (desplegada a LocalStack), push **por viewport**, **readiness** (`/health/ready`) y graceful shutdown, tests de integración con **Testcontainers**.
>
> Detalle por fase en [AUDIT.md](./AUDIT.md) (AUD-001…022; AUD-020 = decisión de pivote a GCP, AUD-021 = G1 Pub/Sub, AUD-022 = G2 Cloud Storage).

---

## ¿Qué resuelve?

Operar una flota (vehículos, maquinaria, sensores IoT) exige saber **ahora** dónde está
cada activo, en qué estado, y reaccionar a anomalías antes de que escalen. El reto no es
mostrar un dato: es sostener un **flujo de alto volumen** (miles de eventos/seg) de
extremo a extremo —ingesta, procesamiento, almacenamiento, push al navegador— sin que
ninguna capa se caiga ni la UI se congele.

## Objetivos medibles (NFRs)

| Métrica | Objetivo |
|---|---|
| Latencia evento→pantalla (P95) | < 1.5 s bajo carga sostenida |
| Throughput de ingesta sostenido | ≥ 5.000 eventos/seg sin pérdida |
| FPS del dashboard con 500 dispositivos | ~60 fps (sin jank) |
| Bundle inicial Angular (gzip) | < 250 KB (lazy por feature) |

---

## Arquitectura en una imagen

```
Simulador ──HTTP──► Ingestion (API GW + Lambda) ──SNS──► SQS (buffer)
                                                            │
                                                            ▼
                            Workers .NET: dedup · reglas · read models
                              │                    │
                  SignalR ◄───┘                    ├──► SQL (hot: read models + telemetría particionada)
                  │                                └──► S3 (cold: data lake inmutable)
                  ▼
            Angular SPA (dashboard en vivo)

         ElastiCache/Redis: dedup set + SignalR backplane
```

- **Camino caliente** (sub-segundo): read models precalculados + deltas por SignalR.
- **Camino frío**: histórico sobre SQL particionado + S3/Athena. Nunca compite con el caliente.

Las decisiones están registradas como **ADRs** en el [SAD](./SAD-Atalaya.md#5-decisiones-de-arquitectura-adrs).

---

## Stack

| Capa | Tecnología |
|---|---|
| Frontend | Angular (standalone), TypeScript, RxJS, NgRx (estado de app) + Component Store (firehose) |
| Tiempo real | SignalR (WebSocket) + backplane Redis |
| Backend | .NET (Minimal API + Worker Services) |
| Mensajería | hoy **AWS SNS → SQS** · target **GCP Pub/Sub** ([ADR-013](./SAD-Atalaya.md)) |
| Almacenamiento | SQL particionado (PostgreSQL → Cloud SQL) + data lake **S3 → Cloud Storage** |
| Analítica | (Athena) → **BigQuery** |
| Auth | JWT Bearer + RBAC · OIDC (Cognito) → **Identity Platform** |
| Infra | hoy **AWS CDK + LocalStack** · target **Terraform + Cloud Run** (emuladores en dev) |
| Monorepo | Nx |
| Observabilidad | OpenTelemetry, métricas RED |

---

## Estructura del repositorio

> Monorepo **Nx 21** (Angular 20) + solución .NET 8.

```
atalaya/
├─ apps/
│  ├─ atalaya-web/      # SPA Angular: shell + features lazy; mapa en vivo (canvas) + tabla
│  ├─ simulator/        # Generador de carga de telemetría (Node)
│  ├─ api/              # .NET Minimal API + SignalR: ingesta→SNS, forwarders Redis→SignalR, read models, health
│  ├─ worker/           # .NET Worker: SQS → dedup → read models + incidentes + camino frío (telemetry+S3) + retención
│  └─ api.tests/        # xUnit: InMemory + lógica pura + Testcontainers (Postgres real)
├─ libs/
│  ├─ contracts/        # DTOs + reglas (TelemetryEvent, DeviceState, AlertIncident, AlertRules, IncidentTransitions)
│  ├─ persistence/      # Postgres: device_state + alert_incidents + telemetry particionada + retención (Dapper/Npgsql)
│  └─ realtime/         # Redis: dedup (ADR-006) + broadcasters pub/sub deltas/alertas (ADR-002)
├─ infra/               # docker-compose (LocalStack+Redis+Postgres) + cdk/ (AWS CDK, ADR-009)
├─ Atalaya.sln, nuget.config
├─ SAD-Atalaya.md       # Documento de arquitectura (rector)
├─ README.md            # Este archivo
├─ CLAUDE.md            # Contexto persistente entre sesiones de trabajo
├─ AUDIT.md             # Bitácora de auditorías por fase/cambio
├─ DEPLOY.md            # Guía de despliegue (local y AWS)
└─ TROUBLESHOOTING.md   # Errores encontrados y sus soluciones
```

---

## Empezar

Prerequisitos: **Node ≥ 20**, **.NET SDK 8**, **Docker Desktop** (todos ✅ en el entorno actual).

### Ver el pipeline completo en vivo (4 terminales)

```bash
npm install
docker compose -f infra/docker-compose.yml up -d   # 1) infra: LocalStack + Redis + Postgres

npx nx serve api                                    # 2) API  → http://localhost:3000
npx nx serve worker                                 # 3) Worker (consume SQS)
npm start                                           # 4) Dashboard → http://localhost:4200
```

Luego inyecta carga y observa el dashboard moverse en vivo:

```bash
npx nx build simulator
node dist/apps/simulator/main.js --rate 2000 --devices 100 --duration 30 \
  --url http://localhost:3000/ingest --token dev-ingest-token
```
> En modo Aws `/ingest` exige el header `X-Ingest-Token` (auth de ingesta). El simulador lo
> envía con `--token`. En modo InMemory no hay token (config base vacía).
>
> **Auth de lecturas** (modo Dev en Development, [AUD-019](./AUDIT.md)): `/api/devices|alerts|history`
> y el hub exigen un **JWT** con rol operador/admin. El dashboard obtiene el token solo
> (`GET /auth/dev-token`); sin token, las lecturas devuelven `401`. Flag `Auth:Mode`
> (`Disabled`/`Dev`/`Oidc`): en prod se valida contra Cognito sin tocar el código.

### Sin Docker (camino rápido)

```bash
ASPNETCORE_ENVIRONMENT="" npx nx serve api   # transporte InMemory: procesa en la API
npm start                                    # dashboard
node dist/apps/simulator/main.js --rate 1000 --devices 50 --duration 10 --url http://localhost:3000/ingest
```

### Verificación

```bash
npx nx run-many -t build       # Angular + simulador + .NET
npx nx run-many -t lint test   # lint + tests (front)
npx nx test api-tests          # .NET: 42/42 con Docker (3 de Testcontainers); 39 + 3 saltados sin Docker
```
Readiness: `curl localhost:3000/health/ready` (API) y `localhost:3100/health/ready` (worker).

Más detalle (modos, endpoints, rollback) en [DEPLOY.md](./DEPLOY.md).

---

## Roadmap

| Fase | Alcance | Estado |
|---|---|---|
| **0 — Cimientos** | Monorepo Nx, esqueleto Angular + .NET, simulador, CI | ✅ |
| **1 — Camino caliente** | Ingesta → SNS/SQS → worker → dedup(Redis) → Postgres → SignalR → dashboard | ✅ E2E |
| **1.5 — Endurecimiento** | Reconexión sin huecos, latencia OTel + P95, push endurecido, auth de ingesta, carga k6 | ✅ ([AUD-009](./AUDIT.md)) |
| **— Ingesta desacoplada** | `/ingest` encola y responde 202; batch a SNS en background | ✅ p95 34 s→34 ms ([AUD-010](./AUDIT.md)) |
| **2 — Alertas + camino frío** | Alertas (incidentes), telemetría particionada, S3 data lake, histórico | ✅ ([AUD-011](./AUDIT.md), [AUD-012](./AUDIT.md), [AUD-017](./AUDIT.md)) |
| **— Productivización** | IaC con **AWS CDK** + grupos por **viewport** en SignalR | ✅ ([AUD-013](./AUDIT.md), [AUD-014](./AUDIT.md)) |
| **2.5 — Calidad de datos** | Retención `DROP PARTITION` + S3 idempotente + alertas como incidentes con histéresis | ✅ ([AUD-016](./AUDIT.md), [AUD-017](./AUDIT.md)) |
| **3 — Endurecimiento operativo** | Readiness/health, graceful shutdown, Testcontainers | ✅ ([AUD-018](./AUDIT.md)) |
| **3 — Seguridad: auth de lecturas** | JWT Bearer flag-gated (Dev HS256 / Oidc JWKS) + RBAC operador/admin en `/api/*` y el hub | ✅ ([AUD-019](./AUDIT.md)) |

### Pivote a GCP — despliegue real ([ADR-013](./SAD-Atalaya.md) / [AUD-020](./AUDIT.md))

> Decisión tomada; **nada migrado aún**. Se desarrolla con emuladores locales (costo $0) y se valida en la nube real.

| Fase | Alcance | Estado |
|---|---|---|
| **G0 — Fundaciones** | Proyecto GCP + **Budget+Alert** + APIs + service accounts | ⬜ |
| **G1 — Mensajería Pub/Sub** | `PubSubBatchPublisher` + consumidor (flag `Telemetry:Transport=Gcp`), E2E contra el emulador | ✅ ([AUD-021](./AUDIT.md)) |
| **G2 — Cloud Storage + camino frío** | `GcsRawEventArchive` (fake-gcs local) + Cloud SQL | ✅ ([AUD-022](./AUDIT.md)) |
| **G3 — Auth Identity Platform** | `Auth:Mode=Oidc` real + login Angular (Firebase Auth) + roles por claims | ⬜ |
| **G4 — BigQuery** | Data lake GCS → BigQuery (cierra Athena) | ⬜ |
| **G5 — IaC + despliegue** | Terraform + **Cloud Run** (API+worker) + SPA a **Firebase Hosting** | ⬜ |
| **G6 — Medición + teardown** | k6 contra Pub/Sub real + script de destrucción de recursos | ⬜ |
| **Backlog (aplica en GCP)** | DLQ replay, downsampling, virtual scroll + mapa real (deck.gl), login real/refresh | ⬜ ([AUD-015](./AUDIT.md)) |

Cada fase entrega algo demostrable y medido. Ver detalle en el
[SAD §13](./SAD-Atalaya.md#13-roadmap-por-fases) y la revisión crítica [AUD-015](./AUDIT.md).

---

## Documentación viva

Estos documentos se mantienen, no se escriben una vez:

- **[AUDIT.md](./AUDIT.md)** — qué se auditó en cada fase y qué se encontró.
- **[DEPLOY.md](./DEPLOY.md)** — cómo se levanta y se despliega.
- **[TROUBLESHOOTING.md](./TROUBLESHOOTING.md)** — errores ya resueltos, para no repetirlos.
- **[CLAUDE.md](./CLAUDE.md)** — contexto para retomar el trabajo sin perder hilo.
