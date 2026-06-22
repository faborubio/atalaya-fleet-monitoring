# Atalaya

> **Plataforma de monitoreo y alertamiento de flota/IoT en tiempo real.**
> Angular + .NET + AWS, arquitectura orientada a eventos.

Atalaya es la torre de vigía: ingiere telemetría de miles de dispositivos, mantiene
proyecciones en vivo y dispara alertas en sub-segundos. Es un proyecto **full-stack con
foco frontend**: el backend existe para que el frontend en tiempo real tenga algo real
que estresar.

| | |
|---|---|
| **Versión** | 0.1.1 |
| **Estado** | 🟢 Fases 1 + 1.5 completas — camino caliente sobre infra real + endurecimiento |
| **Autor** | Fabián Rubio — Full Stack (foco Frontend / Angular) |
| **Repositorio** | https://github.com/faborubio/atalaya-fleet-monitoring |
| **Documento rector** | [SAD-Atalaya.md](./SAD-Atalaya.md) |

> **Lo que ya funciona (verificado E2E):** `simulador → API → SNS → SQS → worker →
> dedup(Redis) → Postgres (read model) → Redis → API → SignalR → dashboard Angular en vivo`,
> reproducible en local con Docker. Ver [AUDIT.md](./AUDIT.md) (AUD-001…006).

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
│  ├─ api/              # .NET Minimal API + SignalR: ingesta→SNS, reenvío Redis→SignalR, lee read model
│  ├─ worker/           # .NET Worker Service: consume SQS → dedup → Postgres → publica deltas
│  └─ api.tests/        # xUnit: test de integración del camino caliente (sin Docker)
├─ libs/
│  ├─ contracts/        # DTOs .NET compartidos (TelemetryEvent, DeviceState)
│  ├─ persistence/      # Read model en Postgres (Dapper/Npgsql)
│  └─ realtime/         # Redis: dedup (ADR-006) + broadcaster pub/sub (ADR-002)
├─ infra/               # docker-compose: LocalStack (SNS/SQS/S3) + Redis + Postgres
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
npx nx test api-tests          # test de integración del camino caliente (.NET, sin Docker)
```

Más detalle (modos, endpoints, rollback) en [DEPLOY.md](./DEPLOY.md).

---

## Roadmap

| Fase | Alcance | Estado |
|---|---|---|
| **0 — Cimientos** | Monorepo Nx, esqueleto Angular + .NET, simulador, CI | ✅ |
| **1 — Camino caliente** | Ingesta → SNS/SQS → worker → dedup(Redis) → Postgres → SignalR → dashboard | ✅ Sobre infra real, verificado E2E |
| **1.5 — Endurecimiento** | Reconexión sin huecos, latencia OTel + P95, push endurecido, auth de ingesta, carga k6 | ✅ Hecho ([AUD-009](./AUDIT.md)); ⚠️ ingesta topa ~1k ev/s vs LocalStack (bottleneck documentado) |
| **2 — Alertas + camino frío** | Reglas + read model de alertas, S3 data lake, telemetría particionada, histórico | ⬜ Pendiente |
| **3 — Endurecimiento avanzado** | DLQ/replay, ingesta serverless a 5k ev/s, backplane SignalR nativo, CDK, seguridad OIDC | ⬜ Pendiente |

Cada fase entrega algo demostrable y medido. Ver detalle en el
[SAD §13](./SAD-Atalaya.md#13-roadmap-por-fases).

---

## Documentación viva

Estos documentos se mantienen, no se escriben una vez:

- **[AUDIT.md](./AUDIT.md)** — qué se auditó en cada fase y qué se encontró.
- **[DEPLOY.md](./DEPLOY.md)** — cómo se levanta y se despliega.
- **[TROUBLESHOOTING.md](./TROUBLESHOOTING.md)** — errores ya resueltos, para no repetirlos.
- **[CLAUDE.md](./CLAUDE.md)** — contexto para retomar el trabajo sin perder hilo.
