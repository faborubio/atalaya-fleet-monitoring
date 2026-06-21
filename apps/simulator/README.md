# simulator

Generador de carga de telemetría de Atalaya (SAD §1.3). Sustituye a dispositivos físicos:
produce eventos de flota a un ritmo configurable y, si se indica un endpoint, los envía por
HTTP. Sin endpoint corre "en seco" mostrando solo métricas — útil ya en Fase 0.

## Uso

```bash
npx nx build simulator
node dist/apps/simulator/main.js --rate 2000 --devices 100 --duration 10
```

| Flag | Env | Default | Descripción |
|---|---|---|---|
| `--rate` | `SIM_RATE` | 1000 | Eventos por segundo (total) |
| `--devices` | `SIM_DEVICES` | 200 | Número de dispositivos simulados |
| `--duration` | `SIM_DURATION` | 0 | Segundos (0 = indefinido) |
| `--url` | `INGEST_URL` | — | Endpoint de ingesta; vacío = dry-run |

## Modelo de evento

Cada evento incluye `eventId` (dedup/idempotencia, ADR-006), `seq` (replay de gaps),
posición (`lat`/`lng`), y métricas (`speedKmh`, `headingDeg`, `fuelPct`, `engineTempC`).
Ver [`src/lib/types.ts`](./src/lib/types.ts).
