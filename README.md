# Atalaya

> **Plataforma de monitoreo y alertamiento de flota/IoT en tiempo real.**
> Angular + .NET + AWS, arquitectura orientada a eventos.

Atalaya es la torre de vigía: ingiere telemetría de miles de dispositivos, mantiene
proyecciones en vivo y dispara alertas en sub-segundos. Es un proyecto **full-stack con
foco frontend**: el backend existe para que el frontend en tiempo real tenga algo real
que estresar.

| | |
|---|---|
| **Versión** | 0.3.0 |
| **Estado** | 🟢 Fases 0→3 completas — feature-complete y verificado E2E (resto: solo-AWS-real + pulido) |
| **Autor** | Fabián Rubio — Full Stack (foco Frontend / Angular) |
| **Repositorio** | https://github.com/faborubio/atalaya-fleet-monitoring |
| **Documento rector** | [SAD-Atalaya.md](./SAD-Atalaya.md) |

> **Lo que funciona (verificado E2E, reproducible en local con Docker):**
> - **Camino caliente:** `simulador → API → SNS → SQS → worker → dedup(Redis) → Postgres → Redis → SignalR → dashboard en vivo`. Ingesta desacoplada (p95 `/ingest` ~34 ms, ~5.000 ev/s sin pérdida).
> - **Alertas como incidentes** con histéresis (abrir/escalar/resolver, no por-evento) y push en vivo.
> - **Camino frío:** telemetría particionada por tiempo (retención por `DROP PARTITION`) + data lake S3 idempotente + vista histórica.
> - **Productivización:** infra como **AWS CDK** (desplegada a LocalStack), push **por viewport**, **readiness** (`/health/ready`) y graceful shutdown, tests de integración con **Testcontainers**.
>
> Detalle por fase en [AUDIT.md](./AUDIT.md) (AUD-001…018).

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
| Mensajería | AWS SNS → SQS (FIFO donde importa el orden) |
| Almacenamiento | SQL particionado (PostgreSQL/TimescaleDB) + S3 data lake |
| Infra | AWS CDK + LocalStack (dev) |
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
npx nx test api-tests          # .NET: 32/32 con Docker (3 de Testcontainers); 29 + 3 saltados sin Docker
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
| **Resto (incremental / AWS real)** | Auth OIDC lecturas, DLQ replay, downsampling, deck.gl · **Athena** (cuenta AWS) | ⬜ Backlog ([AUD-015](./AUDIT.md)) |

Cada fase entrega algo demostrable y medido. Ver detalle en el
[SAD §13](./SAD-Atalaya.md#13-roadmap-por-fases) y la revisión crítica [AUD-015](./AUDIT.md).

---

## Documentación viva

Estos documentos se mantienen, no se escriben una vez:

- **[AUDIT.md](./AUDIT.md)** — qué se auditó en cada fase y qué se encontró.
- **[DEPLOY.md](./DEPLOY.md)** — cómo se levanta y se despliega.
- **[TROUBLESHOOTING.md](./TROUBLESHOOTING.md)** — errores ya resueltos, para no repetirlos.
- **[CLAUDE.md](./CLAUDE.md)** — contexto para retomar el trabajo sin perder hilo.
