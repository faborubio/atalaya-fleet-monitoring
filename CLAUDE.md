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
| [SAD-Atalaya.md](./SAD-Atalaya.md) | Arquitectura rectora (ADR-001…010, NFRs, roadmap) |
| [README.md](./README.md) | Visión, stack, estructura, cómo empezar |
| [AUDIT.md](./AUDIT.md) | Bitácora de auditorías por fase/cambio |
| [DEPLOY.md](./DEPLOY.md) | Despliegue local (LocalStack) y AWS (CDK) |
| [TROUBLESHOOTING.md](./TROUBLESHOOTING.md) | Errores encontrados y soluciones |
| **CLAUDE.md** | Este archivo: contexto entre sesiones |

## 5. Estado actual

**Fecha de actualización:** 2026-06-21
**Fase:** 0 — Cimientos (en curso)

### Hecho
- ✅ SAD v1.0.0 aprobado y leído.
- ✅ Documentos base creados (README, AUDIT, DEPLOY, TROUBLESHOOTING, CLAUDE).
- ✅ `git init` + `.gitignore` + commits.
- ✅ **Monorepo Nx 21.6.11** (Angular 20.3, RxJS 7.8, TS 5.9). Se fijó Nx 21 por
  incompatibilidad de Nx 23 con Angular ([TS-003](./TROUBLESHOOTING.md#ts-003--nx-23-incompatible-con-angular-ts-solution-setup)).
- ✅ App `atalaya-web`: shell + 4 rutas lazy (mapa/dispositivos/alertas/históricos), `OnPush`.
  Build 71 KB transfer, lint y tests verdes.
- ✅ App `simulator` (Node): generador de carga de telemetría (modelo SAD §6), ~1800 ev/s en seco.
- ✅ Auditorías [AUD-001](./AUDIT.md#aud-001--estado-inicial-del-repositorio-y-toolchain-2026-06-21)
  y [AUD-002](./AUDIT.md#aud-002--scaffold-fase-0-monorepo-nx--angular--simulador-2026-06-21).

### Comandos útiles
- `npm start` → sirve `atalaya-web` (http://localhost:4200)
- `node dist/apps/simulator/main.js --rate 2000 --devices 100 --duration 5` → simulador en seco
- `npx nx run-many -t lint test build` → verificación completa

### Bloqueado (prerequisitos por instalar)
- 🔴 **Backend .NET** — falta .NET SDK 8 → [TS-001](./TROUBLESHOOTING.md#ts-001--no-hay-net-sdk-solo-runtime).
- 🔴 **Infra CDK/LocalStack** — falta Docker Desktop → [TS-002](./TROUBLESHOOTING.md#ts-002--docker-no-disponible).

### Toolchain verificado (2026-06-21)
- ✅ git 2.51 · Node v24.15 · npm 11.14 · Nx 21.6.11
- 🔴 .NET: solo runtime 6.0.36, **sin SDK** · 🔴 Docker: no instalado

### Estructura actual del repo
```
atalaya/
├─ apps/
│  ├─ atalaya-web/   # SPA Angular (shell + features lazy)
│  └─ simulator/     # generador de carga de telemetría (Node)
├─ *.md              # SAD, README, AUDIT, DEPLOY, TROUBLESHOOTING, CLAUDE
└─ nx.json, package.json, tsconfig.base.json, eslint.config.mjs
```
Pendientes de crear (bloqueados): `apps/api` (.NET Minimal API), `apps/worker` (.NET),
`infra/` (AWS CDK).

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
- **ADR-010:** Frontend de alto rendimiento: OnPush + Signals + coalescencia por frame.

## 7. Próximos pasos sugeridos

1. Inicializar git y primer commit con la documentación.
2. Generar monorepo Nx + esqueleto Angular (standalone, routing lazy por feature) +
   simulador de telemetría en Node.
3. Cuando se instalen prerequisitos: scaffold backend .NET (TS-001) e infra CDK/LocalStack
   (TS-002).
4. Avanzar a **Fase 1 — camino caliente** (ingesta → cola → worker → SignalR → dashboard).

> Al cerrar cada sesión: actualiza §5 (estado), añade entrada en AUDIT.md si hubo cambio
> auditable, y registra en TROUBLESHOOTING.md cualquier error resuelto.
