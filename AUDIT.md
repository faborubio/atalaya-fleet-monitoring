# AUDIT.md — Bitácora de auditorías

Registro cronológico de auditorías de Atalaya. Cada fase, etapa o cambio relevante deja
una entrada aquí: **qué se revisó, qué se encontró, qué se decidió**. Sirve para tener
trazabilidad técnica y para que cualquier sesión futura sepa en qué estado real está el
proyecto.

## Cómo usar este archivo

- Una entrada por auditoría, **la más reciente arriba**.
- Cada entrada lleva un ID `AUD-NNN`, fecha y alcance.
- Hallazgos clasificados por severidad: 🔴 Crítico · 🟠 Alto · 🟡 Medio · 🔵 Bajo · ✅ OK.
- Los errores accionables se enlazan con [TROUBLESHOOTING.md](./TROUBLESHOOTING.md);
  las decisiones de arquitectura, con los ADR del [SAD](./SAD-Atalaya.md).

### Plantilla

```markdown
## AUD-NNN — <título> (YYYY-MM-DD)

**Fase:** <Fase X / cambio>
**Alcance:** <qué se auditó>
**Auditor:** <quién>

### Hallazgos
| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟠  | ...      | ...    | Abierto/Resuelto |

### Verificaciones
- [ ] ...

### Conclusión
<resumen y veredicto>
```

---

## AUD-005 — Infraestructura de desarrollo en Docker (2026-06-21)

**Fase:** Fase 1→2 (habilitación) — Infra local del pipeline event-driven
**Alcance:** Instalación de Docker, reubicación de su almacén a D:, y `infra/` con
LocalStack (SNS/SQS/S3) + Redis + Postgres vía docker-compose.
**Auditor:** Fabián Rubio + Claude

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅ | **Docker Desktop 29.5.3** instalado (WSL2 ya presente); desbloquea infra | — | Resuelto → [TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible) |
| 🔴 | **C: a 0 bytes** → Docker en modo solo-lectura al pullear | Liberar C: + mover almacén Docker a D: (junction) | Resuelto → [TS-006](./TROUBLESHOOTING.md#ts-006--disco-c-lleno-0-bytes--docker-en-modo-solo-lectura) |
| 🟠 | LocalStack `3.8` corrupto (`exec format error`) y `latest` exige token pro | Fijar `localstack/localstack:3.7` (community) | Resuelto → [TS-007](./TROUBLESHOOTING.md#ts-007--localstack-no-arranca-exec-format-error-y-luego-license-token) |
| ✅ | `infra/docker-compose.yml`: LocalStack + Redis + Postgres, los 3 **healthy** | — | OK |
| ✅ | Recursos creados por init: SNS `atalaya-telemetry`, SQS `...-queue`+`...-dlq` (redrive), S3 `atalaya-datalake`, suscripción SNS→SQS | — | OK |
| ✅ | `.gitattributes` fuerza LF en `*.sh` (evita romper init en clones Windows) | — | OK |
| 🔵 | Recursos provisionados con `awslocal`, no con CDK (ADR-009) | Migrar a AWS CDK más adelante | Abierto |

### Verificaciones

- [x] `docker compose ps` → localstack/redis/postgres **healthy**.
- [x] `awslocal sqs list-queues` / `sns list-topics` / `s3 ls` → recursos presentes.
- [x] Suscripción SNS→SQS con RawMessageDelivery.
- [x] `pg_isready` OK · `redis-cli ping` → PONG.
- [x] Almacén Docker en `D:\DockerData` (C: ya no se llena con imágenes).

### Conclusión

La infraestructura del pipeline corre en local de forma reproducible. El incidente de
disco lleno se resolvió reubicando Docker a D: con un junction; LocalStack quedó fijado a
una versión community íntegra. Con esto, el siguiente paso es **cablear los servicios .NET
a la infra real**: ingesta → SNS/SQS, dedup en Redis, read models en Postgres y backplane
SignalR — sustituyendo los shims en memoria a través de las interfaces ya aisladas.

**Veredicto:** ✅ Infra de desarrollo operativa y verificada.

---

## AUD-004 — Frontend conectado al hub: camino caliente completo (2026-06-21)

**Fase:** Fase 1 — Camino caliente (lazo cerrado: simulador → API → SignalR → dashboard)
**Alcance:** Cliente SignalR en Angular, capa reactiva (FleetStore) con coalescencia,
dashboard con mapa en vivo (canvas) y tabla de dispositivos en vivo.
**Auditor:** Fabián Rubio + Claude

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅ | `TelemetryStreamService`: HubConnection con reconexión automática → stream RxJS (ADR-002) | — | OK |
| ✅ | `FleetStore`: firehose **fuera de NgRx** en capa reactiva con signals (ADR-003) | — | OK |
| ✅ | Coalescencia por ventana de 100 ms antes de tocar el signal — un render por ventana (ADR-010) | — | OK |
| ✅ | Snapshot inicial vía `GET /api/devices` + deltas en vivo; merge por `seq` (orden) | — | OK |
| ✅ | Dashboard: mapa en vivo en **canvas** (sin deps externas) + métricas (count, ev/s, estado) | — | OK |
| ✅ | Devices: tabla en vivo con `trackBy`; ordenada y acotada a 200 filas | — | OK |
| ✅ | **E2E verificado por WebSocket**: cliente recibió 26 lotes = 4.050 eventos / 80 disp = lo enviado, **cero pérdida** | — | OK |
| ✅ | Build 97 KB transfer (NFR < 250 KB ✔), lint y tests OK | — | OK |
| 🔵 | Mapa es canvas básico; falta clustering/WebGL (deck.gl/Mapbox) y CDK Virtual Scroll | Mejora SAD §9 | Abierto |
| 🔵 | URL de API hardcodeada en `devApiConfig` | Externalizar por entorno antes de prod | Abierto |

### Verificaciones

- [x] `nx build atalaya-web` — OK (97 KB transfer).
- [x] `nx lint atalaya-web` / `nx test atalaya-web` — OK.
- [x] Lazo real: API + simulador (1.500 ev/s, 80 disp, 3s) + cliente SignalR → 4.050 eventos recibidos, 0 pérdida.

### Conclusión

**El camino caliente está completo y demostrable de extremo a extremo**: el simulador
inyecta, la API procesa y empuja por SignalR, y el cliente Angular materializa el firehose
con coalescencia y lo pinta `OnPush`. Se respetan los ADR de tiempo real (002/003/004/010).
Quedan mejoras de visualización (WebGL, virtual scroll) y la migración a infraestructura
real (SQS/Redis/SQL) bloqueada por Docker ([TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible)).

**Veredicto:** ✅ Fase 1 (camino caliente, modo dev) completa y verificada end-to-end.

---

## AUD-003 — Backend .NET: API + SignalR + camino caliente en memoria (2026-06-21)

**Fase:** Fase 1 (parcial) — Camino caliente, lado backend
**Alcance:** Instalación de .NET SDK, scaffold de la solución (.NET 8), Minimal API con
ingesta + dedup + read model + hub SignalR, worker skeleton, test de integración e
integración en Nx.
**Auditor:** Fabián Rubio + Claude

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅ | **.NET SDK 8.0.422** instalado; desbloquea backend | — | Resuelto → [TS-001](./TROUBLESHOOTING.md#ts-001--no-hay-net-sdk-solo-runtime) |
| ✅ | Solución `Atalaya.sln`: `contracts` (lib) + `api` + `worker` + `api.tests` | — | OK |
| ✅ | `api`: Minimal API con `POST /ingest` (202, no escribe directo, ADR-001), `GET /api/devices`, `GET /health`, hub SignalR `/hubs/telemetry` | — | OK |
| ✅ | Camino caliente en memoria: bus (canal acotado) → procesador por lotes → dedup (ADR-006) → read model (ADR-005) → push SignalR (ADR-002) | — | OK |
| ✅ | **E2E verificado**: simulador → `/ingest` 2.700 ev en 3s, 0 fallos; `device_state` con 50 dispositivos | — | OK |
| ✅ | Test de integración (WebApplicationFactory, sin Docker): ingesta + dedup + read model | — | OK |
| ✅ | Nx reconoce los 6 proyectos; `nx run-many -t build` (5) y `nx test api-tests` OK | — | OK |
| 🟡 | Sin fuentes NuGet en la máquina | `nuget.config` versionado | Resuelto → [TS-004](./TROUBLESHOOTING.md#ts-004--dotnet-no-resuelve-paquetes-nuget-sin-fuentes) |
| 🟠 | El procesamiento corre **en la API**, no en el `worker` separado (ADR-008) | Mover a `worker` sobre SQS al tener Docker | Abierto (shim de dev documentado) → [TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible) |
| 🟡 | Dedup y read model **en memoria**, sin persistencia | Cambiar a Redis (dedup) + SQL (read model) | Abierto (objetivo Fase 1/2) |

### Verificaciones

- [x] `dotnet build Atalaya.sln` — 0 errores, 0 warnings.
- [x] API arranca en `http://localhost:3000` y procesa ingesta.
- [x] `dotnet test` y `nx test api-tests` — 1/1 OK.
- [x] `nx run-many -t build` — 5 proyectos OK.

### Conclusión

El **camino caliente del backend está vivo y verificado de extremo a extremo** sin
depender de Docker: ingesta desacoplada, dedup idempotente, read model y push SignalR.
Las piezas que requieren infraestructura (cola SQS real en el `worker`, dedup en Redis,
read model en SQL) están **claramente marcadas como shim de dev** y aisladas tras
interfaces (`ITelemetryBus`, `IDeduplicator`, `IDeviceStateStore`) para sustituirlas sin
reescribir la lógica. Falta conectar el **frontend Angular** al hub (siguiente paso) y la
infra (bloqueada por [TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible)).

**Veredicto:** ✅ Backend del camino caliente (modo dev) completo y verificado.

---

## AUD-002 — Scaffold Fase 0: monorepo Nx + Angular + simulador (2026-06-21)

**Fase:** Fase 0 — Cimientos (frontend + simulador)
**Alcance:** Generación del monorepo, app Angular (shell + rutas lazy), simulador de
telemetría en Node, y verificación de build/lint/test.
**Auditor:** Fabián Rubio + Claude

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅ | Monorepo **Nx 21.6.11** (Angular 20.3, RxJS 7.8, TS 5.9) generado limpio | — | OK |
| ✅ | App `atalaya-web`: shell + 4 rutas **lazy** (mapa/dispositivos/alertas/históricos), todo `OnPush` | — | OK |
| ✅ | Build prod: **71 KB transfer** inicial (NFR < 250 KB ✔), code-splitting por feature | — | OK |
| ✅ | `atalaya-web`: lint ✔, 2 tests ✔ | — | OK |
| ✅ | `simulator`: genera telemetría (eventId+seq, modelo SAD §6); ~1800 ev/s en seco; lint ✔, 2 tests ✔ | — | OK |
| 🟡 | Nx 23 incompatible con Angular por *TS solution setup* (project references) | Se fijó **Nx 21** (clásico integrado) | Resuelto → [TS-003](./TROUBLESHOOTING.md#ts-003--nx-23-incompatible-con-angular-ts-solution-setup) |
| 🟡 | `npm audit`: 60 vulnerabilidades transitivas (dev) en el workspace fresco | Revisar con `npm audit`; priorizar high | Abierto |
| 🔵 | Budget de bundle en `project.json` mide *raw* (500 KB warn), no gzip del NFR | Aceptable; vigilar transfer en cada build | Abierto |

### Verificaciones

- [x] `nx build atalaya-web` (producción) — OK, sin warnings de budget.
- [x] `nx lint atalaya-web` / `nx lint simulator` — OK.
- [x] `nx test atalaya-web` / `nx test simulator` — OK (4 tests).
- [x] Simulador ejecutado en seco (`--rate 2000 --devices 100 --duration 3`) — 5.600 eventos, 0 fallos.
- [x] `nx run-many -t lint test build` — OK para los 2 proyectos.

### Conclusión

Fase 0 entrega su parte ejecutable: un monorepo Nx limpio con una SPA Angular estructurada
según el SAD (standalone, lazy, OnPush) y un generador de carga real. Todo compila, pasa
lint y tests. Backend .NET e infra CDK/LocalStack siguen **bloqueados** por prerequisitos
([TS-001](./TROUBLESHOOTING.md#ts-001--no-hay-net-sdk-solo-runtime),
[TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible)).

**Veredicto:** ✅ Fase 0 (frontend + simulador) completa y verificada.

---

## AUD-001 — Estado inicial del repositorio y toolchain (2026-06-21)

**Fase:** Fase 0 — Cimientos (arranque)
**Alcance:** Inventario del repo, lectura del SAD, verificación del entorno de desarrollo
antes de escribir código.
**Auditor:** Fabián Rubio + Claude

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅ | SAD v1.0.0 completo y aprobado (ADR-001…010, NFRs medibles, roadmap por fases) | Tomarlo como rector | OK |
| ✅ | Node.js v24.15.0 y npm 11.14.0 disponibles | Suficiente para Angular/Nx | OK |
| ✅ | git 2.51.2 disponible | Inicializar repo | OK |
| 🔴 | **.NET SDK ausente** — solo runtime 6.0.36, sin SDK | Instalar .NET SDK 8 antes del backend | Abierto → [TS-001](./TROUBLESHOOTING.md#ts-001--no-hay-net-sdk-solo-runtime) |
| 🔴 | **Docker no instalado** | Instalar Docker Desktop antes de LocalStack/Redis/SQS | Abierto → [TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible) |
| 🟡 | Repo sin control de versiones (no es git repo) | `git init` + `.gitignore` | En curso |
| 🟡 | Sin scaffold de código (solo el SAD) | Generar monorepo en Fase 0 | En curso |

### Verificaciones

- [x] SAD leído de extremo a extremo.
- [x] Toolchain verificado (`git`, `node`, `npm`, `dotnet`, `docker`).
- [x] Documentos base creados (README, AUDIT, DEPLOY, TROUBLESHOOTING, CLAUDE).
- [ ] Repositorio git inicializado.
- [ ] Monorepo Nx + esqueleto Angular generado.
- [ ] Backend .NET (bloqueado por SDK).
- [ ] Infra CDK + LocalStack (bloqueado por Docker).

### Conclusión

El proyecto es greenfield: solo existía el SAD. La arquitectura está bien definida y se
toma como contrato. **Dos bloqueos duros** impiden completar la Fase 0 íntegra hoy: falta
el **.NET SDK** (backend) y **Docker** (infra/LocalStack). Se procede con la parte
ejecutable —documentación, git y el **frontend Angular**, que además es el foco de
evaluación— y se difiere backend e infra hasta resolver los bloqueos. Sin avances
inventados: lo que quede bloqueado se marca como bloqueado.

**Veredicto:** ✅ Apto para arrancar Fase 0 (subconjunto frontend). Backend/infra en
espera de prerequisitos.
