# Atalaya

> **Plataforma de monitoreo y alertamiento de flota/IoT en tiempo real.**
> Angular + .NET + AWS, arquitectura orientada a eventos.

Atalaya es la torre de vigía: ingiere telemetría de miles de dispositivos, mantiene
proyecciones en vivo y dispara alertas en sub-segundos. Es un proyecto **full-stack con
foco frontend**: el backend existe para que el frontend en tiempo real tenga algo real
que estresar.

| | |
|---|---|
| **Versión** | 0.0.0 (pre-Fase 0) |
| **Estado** | 🟡 En construcción — Fase 0 (cimientos) |
| **Autor** | Fabián Rubio — Full Stack (foco Frontend / Angular) |
| **Documento rector** | [SAD-Atalaya.md](./SAD-Atalaya.md) |

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

> Monorepo **Nx 21**. El frontend y el simulador ya existen; backend e infra se añaden
> al desbloquear sus prerequisitos (ver [AUDIT.md](./AUDIT.md)).

```
atalaya/
├─ apps/
│  ├─ atalaya-web/      # SPA Angular: shell + features lazy (mapa, dispositivos, alertas, históricos)
│  └─ simulator/        # Generador de carga de telemetría (Node)
│  ├─ api/    ⛔         # .NET Minimal API + hub SignalR (pendiente, falta SDK)
│  └─ worker/ ⛔         # .NET Worker Service / consumidor SQS (pendiente)
├─ infra/     ⛔         # AWS CDK (pendiente, falta Docker)
├─ SAD-Atalaya.md       # Documento de arquitectura (rector)
├─ README.md            # Este archivo
├─ CLAUDE.md            # Contexto persistente entre sesiones de trabajo
├─ AUDIT.md             # Bitácora de auditorías por fase/cambio
├─ DEPLOY.md            # Guía de despliegue (local y AWS)
└─ TROUBLESHOOTING.md   # Errores encontrados y sus soluciones
```

---

## Empezar

```bash
npm install

# Frontend (dashboard en http://localhost:4200)
npm start                 # = nx serve atalaya-web

# Simulador de telemetría (en seco, sin backend aún)
npx nx build simulator
node dist/apps/simulator/main.js --rate 2000 --devices 100 --duration 10
# con backend (Fase 1): INGEST_URL=http://localhost:3000/ingest node dist/apps/simulator/main.js --rate 5000

# Verificación completa
npx nx run-many -t lint test build
```

Prerequisitos pendientes (para backend e infra):

- Node.js ≥ 20 y npm ≥ 10 ✅
- **.NET SDK 8** ⛔ (ver [TROUBLESHOOTING.md](./TROUBLESHOOTING.md#ts-001--no-hay-net-sdk-solo-runtime))
- **Docker Desktop** ⛔ (ver [TROUBLESHOOTING.md](./TROUBLESHOOTING.md#ts-002--docker-no-disponible))

Más detalle en [DEPLOY.md](./DEPLOY.md).

---

## Roadmap

| Fase | Alcance | Estado |
|---|---|---|
| **0 — Cimientos** | Monorepo Nx, esqueleto Angular + .NET, CDK base, simulador, CI | 🟡 En curso |
| **1 — Camino caliente** | Ingesta → SNS/SQS → worker → SignalR → dashboard en vivo | ⬜ Pendiente |
| **2 — Alertas + camino frío** | Reglas + read model de alertas, histórico, S3/Athena | ⬜ Pendiente |
| **3 — Endurecimiento** | DLQ/replay, OTel, prueba de carga 5k ev/s, seguridad | ⬜ Pendiente |

Cada fase entrega algo demostrable y medido. Ver detalle en el
[SAD §13](./SAD-Atalaya.md#13-roadmap-por-fases).

---

## Documentación viva

Estos documentos se mantienen, no se escriben una vez:

- **[AUDIT.md](./AUDIT.md)** — qué se auditó en cada fase y qué se encontró.
- **[DEPLOY.md](./DEPLOY.md)** — cómo se levanta y se despliega.
- **[TROUBLESHOOTING.md](./TROUBLESHOOTING.md)** — errores ya resueltos, para no repetirlos.
- **[CLAUDE.md](./CLAUDE.md)** — contexto para retomar el trabajo sin perder hilo.
