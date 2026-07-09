# SECURITY.md — Postura de seguridad de Atalaya

> ¿De qué me protejo y cómo? — **proporcional** a lo que Atalaya es: un proyecto de portafolio
> (monitoreo IoT en tiempo real, Angular/.NET/GCP) con **datos sintéticos**, sin PII ni usuarios
> reales. El rigor se calibra a esa escala (principio de proporcionalidad del método): controles
> reales donde hay superficie real, sin ceremonial de SIEM/SOAR que este proyecto no necesita.
>
> Se actualiza **al cambiar la postura o la superficie de ataque**. Última revisión: **2026-07-09**
> (revisión de ciberseguridad + triage de la alerta de secret scanning, [AUD-033](./AUDIT.md)).

---

## 1. Modelo de amenazas (proporcional)

| Nos protegemos de | No es objetivo (fuera de alcance por escala) |
|---|---|
| Abuso de endpoints públicos (ingesta, lecturas, hub) — cuota/costo, spam | Adversario estatal / APT |
| Lectura no autorizada de proyecciones (device state, alertas, histórico) | Cumplimiento formal (PCI/HIPAA/ISO) — no hay datos regulados |
| Fuga de secretos reales (SA keys, connection strings, tokens de ingesta) | Pentest continuo / red team |
| Envenenamiento del pipeline (poison pill, replay malicioso) | Datos de usuario / PII (no existen: telemetría sintética) |
| Explotación de dependencias con vulnerabilidades conocidas | Multi-tenant / aislamiento entre clientes |
| Abuso de la Web API Key de Firebase (alta masiva de cuentas, cuota) | |

**Activo más sensible:** la **factura de GCP** (Cloud Run, BigQuery pay-per-byte, Identity Platform).
El daño realista de un ataque a este proyecto es **económico** (consumo de cuota), no de datos.

---

## 2. Superficie de ataque y controles

| Superficie | Exposición | Control |
|---|---|---|
| `POST /ingest` | Pública | **Token de dispositivo** (`X-Ingest-Token`) + **rate limiting** (fixed-window, `Ingest:RatePerSecond`). Compara string en memoria; nunca toca BD ([AUDIT §3.5](./AUDIT.md)) |
| `GET /api/devices\|alerts\|history\|analytics` | Pública | **JWT Bearer + RBAC** (`read`: operador/admin) flag-gated `Auth:Mode` (ADR-012). Sin token → 401 |
| `POST /api/admin/dlq/replay` | Pública | **RBAC `admin`** + solo modo Gcp |
| Hub SignalR `/hubs/telemetry` | Pública (WebSocket) | Token por `?access_token=`; `.RequireAuthorization("read")` si auth activa |
| CORS | — | Orígenes explícitos (`Cors:Origins`); nada de `*` con credenciales |
| Data lake (GCS) / BigQuery | Privado | IAM de mínimo privilegio por SA; **cost guard** `MaximumBytesBilled` en analítica (ADR-013, [AUD-024](./AUDIT.md)) |
| Cloud Run (API/worker) | API pública, worker interno | **Identidad de servicio** (ADC, sin keys); SAs de mínimo privilegio; Cloud SQL por socket unix; Memorystore por VPC connector |
| Pub/Sub / cola | Interno | Poison pill → `Ack`+cuarentena forense; DLQ con IAM ([AUDIT §6.13](./AUDIT.md)) |

---

## 3. Gestión de secretos

- **Secretos reales** → **Secret Manager** (connection string de Cloud SQL con `random_password`, token
  de ingesta). Nunca hardcodeados: `infra/terraform/secrets.tf` los deriva de variables/recursos.
- **Sin service-account keys en el repo.** Las apps en Cloud Run usan **ADC** (identidad del servicio).
  `.gitignore` bloquea `*serviceAccount*.json`, `*-firebase-adminsdk-*.json`, `gcp-key*.json`, `*.pem`.
- **Claves "de firma" de dev/demo** (`Auth:DevSigningKey` en `appsettings.Development` y `terraform-demo`)
  **no son secretos**: firman tokens HS256 de una demo con datos sintéticos, y están anotadas como tal.
- **Firebase Web API Key** → ver §4 (pública por diseño).

---

## 4. Triage: alerta "Google API Key" (secret scanning) — **falso positivo en severidad**

**Qué es:** `apiKey` de **Firebase / Identity Platform** en
[`deploy-target.ts`](./apps/atalaya-web/src/app/core/deploy-target.ts) (junto a `authDomain` + `projectId`).

**Por qué NO es una fuga:** las Firebase Web API Keys son **identificadores públicos por diseño**,
pensados para ir en el cliente y poder commitearse
([docs de Google](https://firebase.google.com/docs/projects/api-keys)). GitHub marca todo patrón
`AIza…` como "Google API Key" sin distinguir una web key (pública) de una Cloud API key con billing
(esa sí sería crítica). **Rotarla no aporta** — la nueva también sería pública.

**Riesgo real y mitigación (abuso, no fuga):** una web key *sin restringir* permite martillar
Identity Platform (alta de cuentas / cuota). Postura requerida:

- [x] **Restringir la key** (GCP Console → Credentials → API restrictions): solo **Identity Toolkit API**. *(Aplicado 2026-07-09, proyecto `fabian-portafolio`.)*
- [x] **Application restrictions = HTTP referrers**: `https://atalaya-demo.web.app/*`,
      `https://atalaya-dashboard.web.app/*`, `http://localhost:4200/*`. *(Aplicado 2026-07-09.)*
- [ ] **Identity Platform**: activar *email enumeration protection*; cerrar signup público si no se usa.
- [ ] **Cerrar la alerta** en GitHub como *"used in code / won't fix — Firebase web key, pública por diseño"*.

> La key vive en el proyecto **`fabian-portafolio`** (no `atalaya-fleet-monitoring`, que es el nombre del repo).
> Los cambios de restricción tardan **hasta ~5 min** en propagar. Marcar arriba al aplicar cada paso.

---

## 5. Postura de dependencias

- **Producción limpia:** `npm audit --omit=dev` = **0 vulnerabilidades**. Lo que se despliega al SPA
  no arrastra vulnerabilidades conocidas.
- **DevDependencies:** los avisos High/Med de `npm audit` (vite, esbuild, ws, koa, piscina…) son
  **build-time**: no llegan a producción. Riesgo real bajo; se atienden en ventanas de mantenimiento.
- **.NET:** deuda cerrada en [AUD-033](./AUDIT.md) — `System.Net.Http`/`System.Text.RegularExpressions`
  4.3.0 (transitivos vía xunit, solo en el **test project**, no desplegado) fijados a versiones parcheadas.
- **Verificación recurrente:** `npm audit --omit=dev` + `dotnet list package --vulnerable --include-transitive`
  forman parte de la batería de "verde" del DoD.

---

## 6. Riesgos aceptados (deuda consciente)

Registrados con su `AUD-NNN` en [AUDIT.md](./AUDIT.md):

- **Durabilidad del borde de ingesta** = best-effort (el 202 es previo a publicar al broker; AUDIT §9.20).
- **DevDependencies con vulns de build** (no shippeadas) — mantenimiento diferido.
- **Web API Key pública** sin restringir a nivel de consola hasta aplicar §4.

---

## 7. Cómo reportar

Proyecto de portafolio personal. Vulnerabilidades: abrir un issue en el repo
(`faborubio/atalaya-fleet-monitoring`) o contacto directo con el autor. Sin bug bounty ni SLA.
