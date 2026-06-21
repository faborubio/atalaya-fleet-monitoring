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
