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

## AUD-027 — Resiliencia: replay de la DLQ de Pub/Sub (2026-06-24)

**Fase:** Backlog de resiliencia (ADR-006, del backlog de [AUD-015](#aud-015)). Aplica al pivote GCP (la DLQ es de Pub/Sub).
**Alcance:** Cerrar el ciclo de la cola de mensajes muertos: la DLQ ya **retenía** los mensajes que agotaron los reintentos; faltaba **re-encolarlos** para reprocesar una vez resuelta la causa. Acción de operación (RBAC admin).
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **Suscripción sobre el topic DLQ** (`atalaya-telemetry-dlq-sub`): sin una suscripción, Pub/Sub no
  retiene los dead-letters. La crea el worker contra el emulador ([GcpPubSubConsumer](./apps/worker/GcpPubSubConsumer.cs))
  y **Terraform** en la nube ([pubsub.tf](./infra/terraform/pubsub.tf), + IAM `pubsub.subscriber` a la SA del API).
- **`IDlqReplayer` / `PubSubDlqReplayer`** ([DlqReplayer.cs](./apps/api/Services/DlqReplayer.cs)): hace
  pull de la suscripción DLQ, **re-publica** cada mensaje crudo al topic principal (que el worker
  reprocesa) y **solo entonces** lo reconoce en la DLQ. Orden publicar→ack ⇒ un fallo no pierde el
  dead-letter (queda sin ack → reentrega); el reproceso es seguro por la idempotencia del pipeline
  (dedup por EventId, clave por hash, máquina de incidentes) — at-least-once (ADR-006).
- **Endpoint** `POST /api/admin/dlq/replay?max=N` ([Program.cs](./apps/api/Program.cs)): solo en modo
  Gcp, **RBAC admin** (política `admin` reservada en AUD-019). Devuelve cuántos re-encoló.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟢  | Cierra el ciclo de resiliencia ADR-006 (DLQ → replay → reproceso idempotente) | `IDlqReplayer` + endpoint admin | Resuelto |
| 🟠  | **El replay se colgaba (revisión crítica):** el bucle "pull hasta vaciar" hacía un pull final sobre la DLQ vacía que se quedaba en **long-poll indefinido** → el endpoint nunca respondía | **Pull único acotado** por deadline (5 s): con mensajes responde en ms; vacío vence limpio → 0. (Se descartó `ReturnImmediately`: ~20 s de reintentos en vacío y una **carrera** que reportaba conteo 0 con mensajes presentes) | Resuelto |
| 🟡  | Conteo 0 aun reprocesando durante las pruebas | Era **contención multi-instancia** (instancias zombie del API compartiendo el pull de la DLQ); con una instancia el conteo es exacto. El reproceso duplicado sería inofensivo (idempotente) | Diagnosticado |
| 🔵  | Pull de hasta 1000/llamada (máximo de Pub/Sub) | Para colas mayores se reinvoca el replay; suficiente para operación | Aceptado (por diseño) |

### Verificaciones

- [x] `dotnet build Atalaya.sln` ✅ · `nx test api-tests` **43/43**.
- [x] **E2E contra el emulador Pub/Sub** (worker+API en modo Gcp, una instancia): se publica un lote
      válido **directo al topic DLQ** (simula un dead-letter retenido); `POST /api/admin/dlq/replay`
      devuelve **`{"replayed":1}` en ~0.06 s** y el dispositivo aparece **reprocesado** en `/api/devices`
      (cadena DLQ→replay→topic principal→worker→read model). Replay sobre DLQ vacía → `0`, acotado por el
      deadline. Suscripción DLQ confirmada en el emulador (`atalaya-telemetry-dlq-sub`).

### Conclusión

La resiliencia at-least-once queda cerrada de punta a punta: lo que cae a la DLQ ya no es un callejón sin
salida, sino algo **operable** (replay admin) y seguro (re-encolar→reproceso idempotente). La revisión
crítica evitó un endpoint que se colgaba en el pull final y fijó el patrón **pull único acotado**.

**Veredicto:** ✅ Cerrado y verificado E2E contra el emulador (incluida la revisión crítica y su fix).

---

## AUD-026 — Frontend: mapa real (deck.gl) + virtual scroll (CDK) (2026-06-24)

**Fase:** Backlog de alto rendimiento de frontend (ADR-010, SAD §9), del backlog de [AUD-015](#aud-015). No es fase del pivote GCP.
**Alcance:** Sustituir el **mapa canvas** (lat/lng normalizado a píxeles, sin geografía) por un **mapa geográfico real con deck.gl** (GPU) y la **tabla** de dispositivos por **virtual scroll** (Angular CDK). Es el ítem del backlog que más encaja con el foco del portafolio (Angular de alto rendimiento).
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **Mapa deck.gl** ([dashboard.ts](./apps/atalaya-web/src/app/features/dashboard/dashboard.ts)): `TileLayer`
  de **OpenStreetMap** (raster, sin API key) como basemap + `ScatterplotLayer` con los dispositivos en
  **lng/lat reales**, coloreados por velocidad (verde→ámbar); fuera del viewport = gris (congelados).
  deck.gl se carga por **dynamic-import** (~1 MB → fuera del bundle inicial y de Jest, patrón de G3).
  Pan/zoom reales (`controller`). Conserva stats (NFR latencia P95), indicador en vivo y los controles
  de viewport (AUD-008). Repaint coalescido (un `setProps` por ventana de 100 ms, ADR-010).
- **Virtual scroll** ([devices.ts](./apps/atalaya-web/src/app/features/devices/devices.ts)):
  `cdk-virtual-scroll-viewport` (Angular CDK) sobre una grilla de divs; **se elimina el recorte de 200
  filas** — ahora solo se renderizan las filas visibles, así escala a miles sin coste de DOM.
- **Deps:** `deck.gl` ^9.1 + `@angular/cdk` ~20.2. Warnings CommonJS de loaders.gl silenciados con
  `allowedCommonJsDependencies` en el build.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟢  | Mapa geográfico real + render GPU + virtual scroll (cierra el ítem ADR-010/AUD-015) | deck.gl + CDK, verificado E2E en navegador real | Resuelto |
| 🟢  | Bundle inicial intacto (deck.gl pesado en chunk lazy del dashboard) | Dynamic-import + ruta lazy; build pasa budgets | Resuelto |
| 🔵  | Tiles OSM públicos (su política desaconseja uso intensivo) | Aceptable para demo; en prod = proveedor con key/atribución reforzada | Aceptado |
| 🔵  | El viewport (AUD-008) sigue recortando por bounds de dispositivos, no por los bounds visibles del mapa | Mantiene la suscripción server-side; mejora futura: derivar el viewport del `viewState` del mapa | Aceptado |

### Verificaciones

- [x] `nx build atalaya-web` (producción) ✅ pasa budgets (deck.gl en chunk lazy) · `nx lint` ✅ · `nx test` ✅ (2/2).
- [x] **E2E en navegador real** (Chrome headless, API en InMemory + simulador, 60 dispositivos):
      **captura del dashboard** muestra el **mapa OSM de CDMX** con los dispositivos geolocalizados y
      coloreados por velocidad + atribución; **captura de `/devices`** muestra la grilla con virtual
      scroll (scrollbar, filas visibles únicamente). Auth Dev (token silencioso) + hub en vivo OK.

### Conclusión

El dashboard pasa de una visualización abstracta a un **mapa geográfico real con render GPU** y una tabla
que **escala a miles de filas** sin coste de DOM — el frontend luce el foco del portafolio (alto rendimiento,
ADR-010) y se mantiene el bundle inicial pequeño (deck.gl lazy). Verificado E2E en navegador real.

**Veredicto:** ✅ Cerrado y verificado E2E (capturas reales del mapa y del virtual scroll).

---

## AUD-025 — Pivote a GCP · G5a: IaC con Terraform + contenedores (Cloud Run) (2026-06-24)

**Fase:** Pivote a GCP ([ADR-013](./SAD-Atalaya.md)), **fase G5** (parte **G5a**) del roadmap de [AUD-020](#aud-020--decisión-pivote-de-nube-aws--gcp-2026-06-23). Reemplaza el AWS CDK como IaC.
**Alcance:** **Autoría del Terraform completo** del plano de control GCP + **Dockerfiles** de api/worker + container-readiness de las apps. Verificación local **$0** (`terraform validate` + build de imágenes). El **`apply` real, verificación E2E en la nube y teardown** se difieren a **G5b** (cobran) — decisión acordada con el usuario por costo.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **Terraform** (`infra/terraform/`, provider `google`/`google-beta` ~> 6.0): Artifact Registry · SAs de
  runtime de mínimo privilegio (api/worker, sin keys — usan la identidad del servicio) · Pub/Sub
  (topic + DLQ + suscripción con `DeadLetterPolicy` **+ IAM del service agent de Pub/Sub**, cerrando el
  gap que G1 dejó "para Terraform") · GCS data lake (lifecycle frío + IAM bucket-scoped) · BigQuery
  dataset + external table NDJSON (declara lo que hacía `bigquery-setup.mjs`) · Cloud SQL Postgres ·
  Memorystore Redis · VPC + **Serverless VPC Access connector** (Cloud Run → Redis) · Secret Manager
  (connection string de Postgres + token de ingesta) · **2× Cloud Run v2** (API público, Worker
  interno) always-on · sitio de Firebase Hosting. Estado remoto en GCS (backend parametrizado).
- **Dockerfiles** multi-stage .NET 8 (`apps/api/Dockerfile`, `apps/worker/Dockerfile`, contexto = raíz,
  restore cacheado por csproj) + `.dockerignore`. API sobre `aspnet:8.0`, worker sobre `runtime:8.0`.
- **Container-readiness (cambios mínimos de app):** (a) `WorkerHealthService` escucha en `http://+:$PORT`
  cuando Cloud Run inyecta `PORT` (en local sigue `localhost:Health:Port`, evita URL ACL en Windows);
  (b) CORS del API configurable por `Cors:Origins` (en dev = `localhost:4200`; en prod = dominio de
  Firebase Hosting). Sin cambios de lógica de dominio.
- **Cómputo always-on (decisión):** worker queda **pull + min-instances=1** (cero cambio de arquitectura);
  API igual (forwarders Redis + SignalR son long-lived). `cpu_idle=false`. Worker `max-instances=1`
  (coherencia de la caché de incidentes, AUD-017).

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟢  | IaC declarativo del plano completo en GCP (reemplaza CDK), `terraform validate` limpio | `infra/terraform/` 16 recursos lógicos | Resuelto |
| 🟢  | El gap de IAM de la DLQ de Pub/Sub (anotado en G1/AUD-021 como "lo hará Terraform") | `pubsub_topic_iam_member`/`subscription_iam_member` al service agent de Pub/Sub | Resuelto |
| 🟠  | **Costo real al desplegar:** Cloud SQL + Memorystore + Cloud Run always-on ≈ US$70–120/mes encendido | Split **G5a (validate, $0)** / **G5b (apply acotado + teardown)**; tiers mínimos; Budget+Alert | Mitigado |
| 🟡  | Memorystore solo expone IP privada → Cloud Run necesita VPC connector (complejidad + costo extra) | `network.tf` (VPC + /28 connector, egress `PRIVATE_RANGES_ONLY`) | Resuelto |
| 🔵  | `google_firebase_project` daría "already exists" (Firebase ya activo desde G3) | No se gestiona aquí; solo el `hosting_site` (documentado) | Aceptado |
| 🔵  | `terraform plan`/`apply` requieren credenciales admin + cobran → no se corren en G5a | Diferido a G5b con runbook en `infra/terraform/README.md` | Aceptado (por diseño) |

### Verificaciones

- [x] `terraform fmt` + `terraform init -backend=false` (providers google/google-beta/random resueltos) +
      **`terraform validate` → "Success! The configuration is valid."**
- [x] `dotnet build Atalaya.sln` ✅ (cambios de WorkerHealthService + CORS) · `nx test api-tests` **43/43**.
- [x] **Build de imágenes Docker** local: API (`atalaya-api:local`) y Worker (`atalaya-worker:local`)
      construyen (restore+publish multi-stage en contenedor).
- [ ] `terraform plan`/`apply` + smoke E2E en la nube + teardown → **G5b** (requiere credenciales admin
      + ventana de costo acotada).

### Conclusión

El despliegue real ya tiene su IaC: un módulo Terraform que describe **todo** el plano de control GCP
(mensajería, datos, cómputo, hosting), validado, con las apps contenerizadas y listas para Cloud Run.
La **revisión crítica** del split por costo evita encender la factura sin necesidad; además el Terraform
**cierra el gap de IAM de la DLQ** que arrastrábamos desde G1. Queda **G5b** (apply + verificación E2E en
la nube + teardown) y **G6** (medición real + script de teardown).

**Veredicto:** ✅ G5a cerrada (IaC escrito y validado, imágenes que construyen). El despliegue vivo es G5b.

---

## AUD-024 — Pivote a GCP · G4: analítica con BigQuery sobre el data lake (2026-06-24)

**Fase:** Pivote a GCP ([ADR-013](./SAD-Atalaya.md)), **fase G4** del roadmap de [AUD-020](#aud-020--decisión-pivote-de-nube-aws--gcp-2026-06-23). Cierra el equivalente a Athena: consultas analíticas sobre el data lake **sin copiar datos**.
**Alcance:** Formato del lake a **NDJSON** + **external table** de BigQuery sobre `gs://atalaya-datalake/raw/*.json` + endpoint **`/api/analytics/devices`** (cliente .NET, RBAC de lectura). Verificado E2E contra **BigQuery real** (`fabian-portafolio`).
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **Formato del lake → NDJSON** ([RawEventKey.cs](./libs/persistence/RawEventKey.cs)): el cuerpo de cada
  objeto crudo pasa de **un array JSON** a **un evento por línea** (`NEWLINE_DELIMITED_JSON`), que es
  lo que BigQuery lee como filas con una *external table* directa. La clave idempotente por hash de
  contenido (`raw/yyyy/MM/dd/{sha256}.json`, AUD-016) **no cambia de semántica** (hash sobre el cuerpo
  NDJSON, sigue determinista). Cambio compartido con el `S3RawEventArchive` heredado (Athena también
  consume line-delimited, así que el camino AWS congelado sigue válido). `IRawEventArchive` es **solo
  escritura** → ningún lector de la app esperaba el array (verificado), el cambio es seguro.
- **Setup BigQuery reproducible** ([scripts/bigquery-setup.mjs](./scripts/bigquery-setup.mjs),
  `@google-cloud/bigquery`): crea el dataset `atalaya_analytics` + la external table `telemetry_raw`
  (esquema explícito camelCase, `ignoreUnknownValues`), idempotente. Sin gcloud/bq CLI (acepta ruta de
  service account key, como `set-role.mjs` en G3). Ubicación configurable (`BQ_LOCATION`, default `US`)
  porque la external table exige que el dataset esté en la **misma ubicación** que el bucket.
- **Endpoint `/api/analytics/devices?minutes&limit`** ([BigQueryAnalyticsQuery.cs](./apps/api/Services/BigQueryAnalyticsQuery.cs)
  / [IAnalyticsQuery.cs](./apps/api/Services/IAnalyticsQuery.cs)): agregados por dispositivo (conteo,
  vel. media/máx, temp máx, combustible mín) desde el lake vía BigQuery. Mismo **RBAC de lectura**
  (`Secured`) que `/api/devices|history`. Se registra **solo si hay dataset configurado**
  (`Gcp:DatasetId`) → ausente en base/tests (sin dependencia de BigQuery). El cliente usa ADC.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟢  | BigQuery lee el lake directamente (Athena-equivalente) tras pasar a NDJSON | External table `NEWLINE_DELIMITED_JSON` + endpoint tras `IAnalyticsQuery` | Resuelto |
| 🟠  | **Costo desbocado (revisión crítica G4):** el layout `yyyy/MM/dd` **no es hive** → no hay poda de particiones; cada consulta escanea **todo el lake** y BigQuery es pay-per-byte → una query desmedida factura sin tope | **Cost guard** `MaximumBytesBilled` (config `Gcp:AnalyticsMaxBytesBilled`, default 1 GB): BigQuery **rechaza** la consulta en vez de facturarla | Resuelto |
| 🟡  | El bucket real `atalaya-datalake` **no existía** (G2 se verificó contra fake-gcs) | El worker lo **auto-crea** al arrancar contra GCS real (`EnsureBucketAsync`); confirmado E2E | Resuelto |
| 🔵  | Un lake ya poblado con objetos en formato array previo quedaría ilegible para la external table (mezcla array+NDJSON) | No aplica aquí (lake real arrancó vacío); a futuro = reprocesar/migrar objetos viejos | Aceptado (N/A hoy) |
| 🔵  | Sin poda de particiones, a gran escala el escaneo total encarece; migrar a layout hive (`dt=YYYY-MM-DD`) lo resolvería (tocaría `RawEventKey`, compartido con S3) | Documentado; aceptable a escala dev/free-tier con el cost guard | Aceptado (por diseño) |

### Verificaciones

- [x] `dotnet build Atalaya.sln` ✅ · `nx test api-tests` **43/43** (test nuevo: el cuerpo del lake es
      NDJSON, una línea por evento parseable).
- [x] **E2E contra GCP real** (`fabian-portafolio`, key de service account): worker en modo Gcp →
      **GCS real** (no fake-gcs): bucket `atalaya-datalake` auto-creado, **objetos NDJSON** bajo
      `raw/2026/06/24/{sha256}.json` (un evento por línea, camelCase, parseable). El script creó
      `atalaya_analytics.telemetry_raw`. El endpoint `/api/analytics/devices` devolvió **agregados por
      dispositivo reales** leídos por BigQuery de la external table (cadena worker→GCS→BigQuery→endpoint).
- [x] **IAM de mínimo privilegio (runtime) descubierto E2E**: la SA `bq-query-service` necesitó, en
      orden, `bigquery.jobs.create` (lanzar query) + `storage.objects.list/get` sobre el bucket (leer
      el lake que la external table referencia) además de leer la tabla. Set final:
      `roles/bigquery.jobUser` + `roles/bigquery.dataViewer` + `roles/storage.objectViewer`.
- [x] **Cost guard**: tras añadir `MaximumBytesBilled` (1 GB) el endpoint sigue devolviendo datos
      (la query escanea bytes mínimos); una consulta que excediera el tope sería rechazada por BigQuery.

### Conclusión

La analítica fría ya es real en GCP: BigQuery consulta el data lake **directamente** (external table
sobre NDJSON), cerrando el equivalente a Athena, y el dashboard tiene una superficie nueva
(`/api/analytics`) detrás del mismo RBAC. La **revisión crítica** añadió un **cost guard** imprescindible
para un servicio pay-per-byte sin poda de particiones. El E2E además sirvió para **fijar el IAM de
runtime de mínimo privilegio** de la SA de consulta. Queda **G5** (Terraform + Cloud Run + Firebase
Hosting) y **G6** (medición real + teardown).

**Veredicto:** ✅ G4 cerrada y verificada E2E contra BigQuery real (incluida la revisión crítica y el cost guard).

⚠️ **Artefactos reales creados** (vigilar budget, limpiar en G6): bucket `gs://atalaya-datalake`,
dataset `fabian-portafolio.atalaya_analytics` + external table `telemetry_raw`. El free-tier de BigQuery
(1 TB consultas/mes) cubre dev de sobra.

---

## AUD-023 — Pivote a GCP · G3: auth OIDC real con Identity Platform (2026-06-24)

**Fase:** Pivote a GCP ([ADR-013](./SAD-Atalaya.md)), **fase G3** del roadmap de [AUD-020](#aud-020--decisión-pivote-de-nube-aws--gcp-2026-06-23). Primer servicio GCP **real** (no emulado).
**Alcance:** `Auth:Mode=Oidc` validado contra **Identity Platform** real + **login Angular** (Firebase
Auth) + **roles por custom claim**. Cierra el OIDC que AUD-019 dejó como swap de config.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **Backend (config, no código nuevo):** el modo Oidc de AUD-019 ya validaba JWT contra un
  authority/JWKS. Se añadió `Auth:ProjectId` que **deriva** `EffectiveAuthority`
  (`https://securetoken.google.com/{projectId}`) y `EffectiveAudience` (`projectId`). El rol viaja
  como **custom claim `role`** (RBAC operador/admin ya existente).
- **Frontend:** `AuthService` con dos estrategias por `AUTH_CONFIG.mode` (`dev` token silencioso /
  `firebase` login real / `disabled`). Componente `Login` (email+password). **Firebase se importa de
  forma dinámica** solo en modo firebase → fuera del bundle inicial (98.9 kB gzip, < NFR 250) y fuera
  del grafo de Jest. El ID token y el rol (custom claim) salen de Firebase, que refresca el token solo.
- **Custom claims:** script `scripts/set-role.mjs` (firebase-admin) que asigna `role`; acepta la ruta
  de una service account key (sin depender de gcloud). No hay UI de consola para esto.
- **Proyecto real:** `fabian-portafolio` (Identity Platform). El `apiKey` web es un identificador
  **público** del proyecto (no secreto) → puede vivir en el cliente/repo.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟢  | OIDC real swap por config (ADR-011/AUD-019): solo `Auth:ProjectId` + login Firebase | `EffectiveAuthority/Audience` derivados del projectId | Resuelto |
| 🟠  | **Ciclo auth↔stream (revisión crítica G3):** `signOut()` no cerraba el WebSocket del hub → la telemetría seguía fluyendo a una sesión deslogueada; los `started` impedían re-login limpio | `disconnect()` en `TelemetryStreamService` + `stop()` en `FleetStore`/`AlertStore`; el `App` ata start/stop al estado de sesión (cablea una vez, conecta/desconecta por login/logout) | Resuelto |
| 🔵  | Interceptor REST usa token cacheado; SignalR no refresca token en conexión abierta >1h | Firebase auto-refresca (`onIdTokenChanged`); al reconectar `ensureToken`→`getIdToken`. Estándar | Aceptado |

### Verificaciones

- [x] `dotnet build Atalaya.sln` + `nx test api-tests` **42/42**; frontend `lint+test+build` ✅ (bundle
      inicial 98.9 kB gzip; firebase en chunks lazy).
- [x] **E2E contra Identity Platform REAL** (`fabian-portafolio`, API en `Auth:Mode=Oidc`,
      `Auth:ProjectId=fabian-portafolio`): discovery + JWKS del issuer alcanzables; se creó un test
      user real y se minteó un ID token de Google. Resultados sobre `/api/devices`:
      **401** sin token · **401** firma inválida · **403** token válido **sin** rol · **200** token
      válido con `role=operador` (claim inspeccionado en el JWT: `iss/aud=fabian-portafolio`).
- [x] Fix de ciclo de vida: build/lint/test verdes; la lógica desmonta el hub al `signOut` y reconecta
      con token fresco al re-login (smoke test interactivo en navegador pendiente del usuario).

### Conclusión

Primer servicio GCP **real** integrado: la auth de lecturas valida tokens de Identity Platform de
verdad (no emulado), con login real en el dashboard y RBAC por custom claim. La revisión crítica cerró
un gap de ciclo de sesión (WebSocket vivo tras logout). Queda **G4** (BigQuery sobre el data lake) y el
despliegue real (G5: Cloud Run + Terraform + swap de configs a producción).

**Veredicto:** ✅ G3 cerrada y verificada E2E contra Identity Platform real (incluida la revisión crítica).

---

## AUD-022 — Pivote a GCP · G2: data lake en Cloud Storage (2026-06-24)

**Fase:** Pivote a GCP ([ADR-013](./SAD-Atalaya.md)), **fase G2** del roadmap de [AUD-020](#aud-020--decisión-pivote-de-nube-aws--gcp-2026-06-23).
**Alcance:** Camino frío crudo en Cloud Storage (reemplaza el `NullRawEventArchive` del modo Gcp) +
Cloud SQL (solo connection string). Verificado contra el emulador **fake-gcs-server** (costo $0).
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **`GcsRawEventArchive`** (`IRawEventArchive`), espejo del `S3RawEventArchive`: vuelca cada lote crudo
  como objeto JSON inmutable bajo `raw/yyyy/MM/dd/{sha256}.json`, **reusando `RawEventKey`** (clave por
  hash de contenido, AUD-016) → una reentrega del mismo lote sobrescribe el mismo objeto. Es la base de
  **BigQuery** en GCP real (G4). En modo Gcp reemplaza al `NullRawEventArchive` de G1.
- **Cliente Storage**: contra fake-gcs se fija `BaseUri` + `UnauthenticatedAccess` explícitos (el
  cliente .NET arma mal la URL desde `STORAGE_EMULATOR_HOST` con fake-gcs → 404 en CreateBucket); en
  GCP real usa `StorageClient.Create()` (credenciales del entorno, ADC).
- **Infra**: servicio `fake-gcs` en docker-compose (perfil `gcp`, `-scheme http`). Config
  `Gcp:Bucket` + `Gcp:StorageEmulatorHost`. **Cloud SQL**: sin código — el Postgres de read models /
  telemetría apunta por connection string a Cloud SQL en prod (G5); en dev sigue el Postgres local.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟢  | Paridad S3↔GCS tras `IRawEventArchive`, reusando la clave idempotente | `GcsRawEventArchive` + `RawEventKey` común | Resuelto |
| 🟠  | **Pérdida de eventos en el data lake (revisión crítica G2):** el dedup marcaba el evento *antes* de los efectos (`SET NX` al filtrar); un fallo transitorio de GCS → `Nack`→redelivery → el reintento lo filtraba como duplicado → hueco permanente en el lake (y se saltaba la evaluación de alertas) | Dedup **check + commit**: `FilterNewAsync` solo consulta (`EXISTS`), `CommitAsync` marca (`SET`) **tras** aplicar los efectos; seguro porque todos son idempotentes | Resuelto |
| 🟡  | El cliente .NET no resuelve bien la URL del emulador desde `STORAGE_EMULATOR_HOST` (404) | `BaseUri`+`UnauthenticatedAccess` explícitos contra fake-gcs | Resuelto |
| 🔵  | `EnsureBucket` es fail-fast al arrancar (un data lake inalcanzable impide arrancar) | Igual que S3; aceptable (fallar rápido ante data lake ausente) | Aceptado (por diseño) |

### Verificaciones

- [x] `dotnet build Atalaya.sln` ✅ · `nx test api-tests`: **42/42** (sin cambios; el archivo GCS es
      glue cubierto por E2E, igual que S3 no tiene unit test).
- [x] **E2E contra fake-gcs** (`docker compose up -d redis postgres pubsub-emulator fake-gcs`, worker en
      `Telemetry:Transport=Gcp`): `POST /ingest`→**202**; el lote aterriza en GCS como
      `raw/2026/06/24/{sha256}.json` (application/json) y su descarga devuelve el lote crudo **exacto**;
      el read model (`/api/devices`) y la métrica `atalaya.telemetry.archived` confirman la cadena
      completa **API→Pub/Sub→worker→Postgres+GCS**.
- [x] **E2E del fix de dedup (sin pérdida):** con fake-gcs **detenido**, un evento entra al read model
      pero su subida a GCS falla (socket refused en el log) → `Nack`/reintento sin confirmar dedup; al
      **restaurar** fake-gcs, el evento se **reprocesa y aterriza** en GCS (`raw/.../{sha256}.json`).
      Con el orden anterior se habría perdido (filtrado como duplicado en el reintento).

### Conclusión

El camino frío crudo ya es real en GCP: el data lake S3 tiene su equivalente GCS sin tocar el dominio
ni la clave idempotente. La **revisión crítica** además cerró una pérdida de eventos pre-existente
(dedup antes de los efectos) que solo se volvía visible con el lake real — ahora dedup **check+commit**
garantiza que un fallo transitorio reprocesa en vez de perder. Queda **G3** (Identity Platform),
**G4** (BigQuery sobre este lake) y el despliegue real (G5). Cloud SQL es swap de connection string (G5).

**Veredicto:** ✅ G2 cerrada y verificada E2E (incluida la revisión crítica y su fix).

---

## AUD-021 — Pivote a GCP · G1: mensajería Pub/Sub tras el flag (2026-06-23)

**Fase:** Pivote a GCP ([ADR-013](./SAD-Atalaya.md)), **fase G1** del roadmap de [AUD-020](#aud-020--decisión-pivote-de-nube-aws--gcp-2026-06-23). Primera implementación real del pivote.
**Alcance:** Adaptador de Google Cloud Pub/Sub (publicador + consumidor) seleccionable por el flag
`Telemetry:Transport=Gcp`, verificado contra el **emulador** local (costo $0).
**Auditor:** Fabián Rubio + Claude

### Qué se hizo (respetando ADR-011: tras las interfaces existentes)

- **API (publicador):** `GcpPubSubBatchPublisher` (BackgroundService) espeja al `SnsBatchPublisher`:
  drena el canal del `QueueingTelemetryPublisher`, coalesce ráfagas y publica al topic por lotes
  (cada mensaje = array JSON de ≤N eventos, **mismo formato de cuerpo** que SNS+RawMessageDelivery,
  así el consumidor no distingue el broker). `QueueingTelemetryPublisher` se generalizó (capacidad
  por valor, ya no depende de `AwsOptions`). Readiness: `PubSubHealthCheck` (topic alcanzable).
- **Worker (consumidor):** `GcpPubSubConsumer` (SubscriberClient de alto nivel: streaming pull,
  concurrencia, ack/nack). El procesamiento de lote se **extrajo** a `TelemetryBatchProcessor`
  (común a SQS y Pub/Sub): dedup → upsert → push → camino frío → incidentes, idéntico e
  independiente del broker. Readiness abstraída en `IWorkerReadiness` (`SqsReadiness`/`PubSubReadiness`);
  `WorkerHealthService` ya no depende de SQS.
- **Topología autoprovisionada** contra el emulador (crea topic+suscripción al arrancar, idempotente);
  en la nube real la creará Terraform (G5). El cliente apunta al emulador vía `PUBSUB_EMULATOR_HOST`
  (de `Gcp:EmulatorHost`), sin credenciales ni coste.
- **Resiliencia de entrega (paridad con SQS, tras revisión crítica del G1):**
  - **DLQ:** la suscripción se crea con `DeadLetterPolicy` (topic `atalaya-telemetry-dlq`,
    `MaxDeliveryAttempts=5`), espejo del redrive de SQS. En GCP real exige IAM al service account de
    Pub/Sub (lo dará Terraform, G5); el emulador acepta la config.
  - **Mensaje envenenado:** el handler distingue **veneno** (cuerpo que no deserializa → `Ack` +
    log + métrica `atalaya.events.poison`, sin reintentar en bucle) de **transitorio** (BD/Redis →
    `Nack`, reintenta; tras 5 va a la DLQ). Evita el redelivery infinito del emulador.
  - **Arranque robusto:** `EnsureTopology` reintenta ante transitorios gRPC (emulador aún
    levantando handshake HTTP/2) en vez de tirar el worker (`BackgroundService`→`StopHost`).
- **Infra:** servicio `pubsub-emulator` en docker-compose bajo perfil `gcp` (no arranca por defecto;
  el flujo AWS queda intacto). Secciones `Gcp` en appsettings de api/worker. Tests fuerzan InMemory.
- **El data lake crudo (S3→GCS) NO entra aún** (es G2): en modo Gcp `IRawEventArchive` = `NullRawEventArchive`.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟢  | Paridad de transporte AWS↔GCP tras las interfaces, sin tocar el dominio | `TelemetryBatchProcessor` común + flag `Gcp` | Resuelto |
| 🟠  | **Sin DLQ ni manejo de veneno** (revisión crítica): la suscripción no tenía dead-letter y el handler hacía `Nack` ante cualquier error → redelivery infinito de un mensaje envenenado | `DeadLetterPolicy` (5 intentos) + handler veneno(`Ack`+métrica)/transitorio(`Nack`) | Resuelto |
| 🟠  | **Arranque frágil:** un transitorio gRPC al crear la topología tiraba el worker entero (`StopHost`) | Reintento acotado en `EnsureTopology` | Resuelto |
| 🟡  | El camino frío crudo no tiene equivalente GCP todavía | `NullRawEventArchive` en modo Gcp; GCS llega en G2 | Abierto (planeado) |
| 🔵  | Transición de incidente puede duplicarse bajo concurrencia (leer→decidir→upsert sin tx) | Pre-existente (AWS N=2), ampliado por Pub/Sub; dedup por eventId protege el read model | Anotado |

### Verificaciones

- [x] `dotnet build Atalaya.sln` ✅ · `nx test api-tests`: **42/42** (39 + **3 nuevos** del troceo Pub/Sub).
- [x] **E2E contra el emulador** (`docker compose up -d redis postgres pubsub-emulator`, api+worker en
      `Telemetry:Transport=Gcp`): `/health/ready`→**200** (gateado por Postgres+Redis+**Pub/Sub**);
      `POST /ingest`→**202**; los eventos atraviesan **API→Pub/Sub→worker→Postgres** y aparecen en
      `/api/devices` con **valores correctos** (`gcp-dev-3` engineTempC=85 sano; `gcp-dev-4`
      engineTempC=120 → incidente **engine-temp-high Critical**); `/api/history` devuelve el archivado.
- [x] **E2E del veneno:** mensaje malformado publicado directo al topic → worker loguea
      "envenenado…se descarta", incrementa `atalaya.events.poison` y **sigue vivo** (sin bucle); un
      evento válido posterior se procesa normal. Topología creada con `DeadLetterPolicy` (5 intentos).
- [x] El flujo AWS (LocalStack) sigue intacto: el branch `Aws` no cambió su comportamiento.

### Conclusión

Primera pieza del pivote en pie y probada de verdad contra un broker real (emulado): el mismo
producto corre sobre Pub/Sub cambiando solo el flag. Confirma que el desacoplamiento por interfaces
(ADR-011) aguanta el cambio de proveedor. Sigue **G2** (GCS + Cloud SQL) y la topología real en G5.

**Veredicto:** ✅ G1 cerrada y verificada E2E contra el emulador.

---

## AUD-020 — Decisión: pivote de nube AWS → GCP (2026-06-23)

**Fase:** Decisión de arquitectura ([ADR-013](./SAD-Atalaya.md)). **Es una decisión + roadmap, NO una
implementación**: a esta fecha **nada está migrado**; el sistema sigue corriendo contra AWS/LocalStack.
**Alcance:** Reorientar el target cloud del portafolio a Google Cloud y planificar la migración.
**Auditor:** Fabián Rubio + Claude

### Qué se decidió

- **Pivote a GCP** (cuenta con presupuesto ~US$200): la narrativa pasa a *"Angular/.NET/GCP
  event-driven con despliegue real"*. Razón: desplegar en una **nube real** pesa más que "probado
  contra LocalStack", y valida que el desacoplamiento por interfaces ([ADR-011](./SAD-Atalaya.md))
  resiste un cambio de proveedor. Mapeo de servicios y trade-offs en [ADR-013](./SAD-Atalaya.md).
- **Se preserva ADR-011**: las implementaciones GCP entran **tras las interfaces existentes**
  (`ITelemetryPublisher`, `IRawEventArchive`, …), por el flag de transporte (nuevo valor `Gcp`), y el
  desarrollo usa **emuladores locales** (Pub/Sub emulator, `fake-gcs-server`) para no gastar; la nube
  real se reserva para validar/demostrar. Tests siguen en `InMemory`.

### Roadmap de ejecución (incremental, cada fase verificable)

| Fase | Qué | Costo dev | Estado |
|------|-----|-----------|--------|
| **G0** | Fundaciones: ADR-013 + docs · proyecto GCP · **Budget+Alert** (tope) · habilitar APIs · service accounts | $0 | ⬜ (docs ✅) |
| **G1** | Mensajería **Pub/Sub**: `PubSubBatchPublisher` + consumidor en worker, tras el flag; E2E contra el **emulador** | $0 | ✅ ([AUD-021](#aud-021--pivote-a-gcp--g1-mensajería-pubsub-tras-el-flag-2026-06-23)) |
| **G2** | **GCS** + camino frío: `GcsRawEventArchive` (fake-gcs local) · Cloud SQL = solo connection string | $0 | ✅ ([AUD-022](#aud-022--pivote-a-gcp--g2-data-lake-en-cloud-storage-2026-06-24)) |
| **G3** | **Auth Identity Platform**: `Auth:Mode=Oidc` real · login en Angular (Firebase Auth) · roles por custom claims | ~$0 | ✅ ([AUD-023](#aud-023--pivote-a-gcp--g3-auth-oidc-real-con-identity-platform-2026-06-24)) |
| **G4** | **BigQuery**: data lake GCS → BigQuery (external/load) · consultas analíticas (cierra Athena) | bajo | ⬜ |
| **G5** | **IaC Terraform** + despliegue **Cloud Run** (API+worker) + SPA a **Firebase Hosting** | medio | ⬜ |
| **G6** | Medición real: k6 contra Pub/Sub **real** · latencia/throughput de verdad · script de **teardown** | bajo | ⬜ |

### Hallazgos / riesgos anotados

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟠  | Cloud SQL/Memorystore cobran por hora **ociosos** → pueden agotar los $200 | **Budget+Alert** (tope) + tiers mínimos + teardown por Terraform; apagar cuando no se demuestra | Abierto (mitigación en G0/G5) |
| 🟡  | SNS/SQS/S3 atados al **AWS SDK** → no es swap por config, hay que escribir adaptadores GCP | Acotado por las interfaces (ADR-011); G1/G2 | Abierto (planeado) |
| 🔵  | La narrativa deja de coincidir *literal* con vacantes "AWS" | Asumido a favor de demostrar despliegue real + portabilidad de proveedor | Aceptado (por diseño) |

### Verificaciones

- [x] Decisión registrada en ADR-013 (SAD v1.0.3) y reflejada en CLAUDE.md / README.
- [ ] G0…G6: pendientes (ver tabla). **Nada migrado a esta fecha.**

### Conclusión

Decisión tomada y documentada; la implementación arranca por **G1 (Pub/Sub contra el emulador,
costo $0)** mientras se hace el setup de GCP (proyecto + Budget+Alert) en paralelo. La fase AWS se
conserva como historia de diseño y como evidencia del swap de proveedor.

**Veredicto:** ✅ Pivote a GCP decidido y planificado (G0…G6). ⬜ Sin implementar aún.

---

## AUD-019 — Fase 3 (seguridad): auth de lecturas con OIDC/JWT + RBAC (2026-06-23)

**Fase:** Fase 3 — Endurecimiento (SAD §10), seguridad. Cierra el item de **mayor peso** del backlog
de [AUD-015](#aud-015--revisión-crítica-tras-fase-2--productivización-brechas-y-mejores-ideas-2026-06-22) (fila 🟠 "Lecturas sin autenticación").
**Alcance:** Autenticar `/api/devices|alerts|history` y el hub SignalR; autorización por rol.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **JWT Bearer flag-gated** (`Auth:Mode`, mismo espíritu que `Telemetry:Transport`, ADR-011):
  `Disabled` (base/tests, sin auth), `Dev` (la API emite y valida tokens **HS256** locales, sin
  cuenta AWS) y `Oidc` (valida contra un **authority/JWKS** real — Cognito — cuando la cuenta esté
  lista). El swap Dev→Oidc **no toca los endpoints**: solo cambia cómo se valida la firma.
- **RBAC operador/admin** (SAD §6.1): política `read` (rol operador o admin) en todas las lecturas
  REST y en el hub; política `admin` reservada para acciones futuras. Claim de rol `role`.
- **Hub por query string**: el WebSocket no manda cabecera `Authorization`; `JwtBearerEvents.
  OnMessageReceived` recoge `?access_token=` solo para rutas `/hubs`. El frontend lo entrega vía
  `accessTokenFactory`.
- **Emisor dev** (`/auth/dev-token?role=`): mintea un JWT con rol; el dashboard lo adquiere de forma
  **silenciosa al arrancar** (auto-token, sin pantalla de login) por `APP_INITIALIZER`, lo adjunta a
  `/api/*` por un interceptor y al hub por `accessTokenFactory`.
- **La ingesta no cambia**: `/ingest` sigue gobernada por el token de dispositivo (`X-Ingest-Token`),
  no por la auth de usuario. `/health/*` quedan libres (orquestadores).

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟠  | Lecturas (`/api/*` + hub) abiertas: cualquiera leía la telemetría de la flota | JWT Bearer + RBAC operador/admin (read policy) | Resuelto |
| 🟡  | El WebSocket no puede mandar `Authorization` | Token por `?access_token=` (`OnMessageReceived`) + `accessTokenFactory` | Resuelto |
| 🟡  | Modo `Dev` usa clave simétrica en `appsettings.Development` (en claro) | Aceptable en dev; en prod = `Oidc` (JWKS) + secretos en SSM/Secrets Manager (AUD-015) | Abierto (por diseño) |
| 🔵  | Sin pantalla de login (auto-token) ni refresh-token real | Suficiente para demostrar la cadena E2E; login/OIDC real queda como incremental | Abierto (por diseño) |

### Verificaciones

- [x] `dotnet build Atalaya.sln` ✅ · `nx run-many -t lint test build --projects=atalaya-web` ✅
- [x] `nx test api-tests`: **39/39** (32 previos + **7 nuevos de auth**), 0 saltados con Docker.
- [x] **E2E contra la API en vivo** (InMemory + `Auth:Dev`): `/api/devices` sin token → **401**;
      `/auth/dev-token` → JWT (257 chars); con token operador → **200**; rol inválido en el emisor →
      **400**; `/api/history` sin `deviceId` (autenticado) → **400**; hub `negotiate` sin token →
      **401**; `/ingest` con `X-Ingest-Token` → **202** (la ingesta no se tocó).
- [x] Tests de auth cubren además el **403** (token válido con rol fuera de la policy, forjado con
      `DevTokenIssuer`) y la **conexión al hub con token por query string**.

### Conclusión

Las lecturas dejan de ser abiertas: exigen un usuario autenticado con rol. La pieza queda
**OIDC-ready** (swap a Cognito por config, sin reescribir) respetando ADR-011. Quedan de Fase 3 los
items menores de seguridad (login real/refresh, secretos gestionados) y el resto del backlog (DLQ
replay, downsampling, virtual scroll/mapa real); y los solo-AWS-real.

**Veredicto:** ✅ Auth de lecturas cerrada (el item de mayor peso de AUD-015).

---

## AUD-018 — Fase 3 (endurecimiento operativo): readiness + graceful shutdown + Testcontainers (2026-06-23)

**Fase:** Fase 3 — Endurecimiento (SAD §10), núcleo operativo. Cierra parte de [AUD-015](#aud-015--revisión-crítica-tras-fase-2--productivización-brechas-y-mejores-ideas-2026-06-22) C/D.
**Alcance:** Health/readiness, apagado ordenado del buffer de ingesta, pruebas del SQL real.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **Readiness gateada por dependencias** (AUD-015 E): API con `/health/live` (liveness, sin deps) y
  `/health/ready` (checks de **Postgres + Redis + SNS**; en InMemory, sano sin checks). Worker con
  endpoint mínimo (`HttpListener`, `/health/live` + `/health/ready` gateado por SQS, best-effort:
  si no enlaza el puerto, el worker sigue). Liveness ≠ readiness → rolling deploy sano.
- **Graceful shutdown** (deuda de AUD-010): al pararse, el `SnsBatchPublisher` **drena** el canal y
  publica lo pendiente a SNS antes de salir (acotado a 5 s), en vez de perder lo *buffered*. El
  worker ya era ordenado (cancelación cooperativa; un lote a medias no se borra → reentrega).
- **Testcontainers** (AUD-015 F): tests de integración contra **Postgres real** para lo que el modo
  InMemory no cubre — particionado, `unnest`, `ON CONFLICT`, retención por `DROP PARTITION` y la
  máquina de incidentes en SQL. **Se saltan** limpiamente si Docker no está (no rompen el suite).

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟠  | Sin readiness real (health no comprobaba deps; worker sin health) | `/health/ready` gateado + health del worker | Resuelto |
| 🟡  | Apagado perdía el buffer de `/ingest` | Drenado del publicador en shutdown (best-effort, 5 s) | Resuelto |
| 🟠  | SQL del camino frío/incidentes sin test automatizado | Testcontainers (Postgres real), skippable sin Docker | Resuelto |
| 🟡  | El drenado de cierre es best-effort (timeout 5 s); a escala real la durabilidad la da API GW→SNS | Documentado (AUD-015 B) | Abierto (por diseño) |

### Verificaciones

- [x] `nx run-many -t build` (5) ✅ · `nx run-many -t lint test` ✅
- [x] `nx test api-tests`: **32/32** con Docker (3 de Testcontainers contra Postgres real); **29 + 3
      saltados** sin Docker.
- [x] **E2E health**: API `/health/live`→200, `/health/ready`→200 con deps arriba y **→503 con Redis
      caído** (readiness gatea de verdad); worker `:3100/health/live`→`live`, `/health/ready`→`ready`.

### Conclusión

El sistema gana postura operativa: readiness real para orquestadores, apagado que no tira lo
buffered y red de seguridad automatizada sobre el código Postgres que antes solo se probaba a mano.
Quedan de Fase 3 (SAD §10) los items de seguridad (OIDC/roles), DLQ replay, virtual scroll/mapa y
runbooks; y los solo-AWS-real (Athena, throughput, multi-entorno).

**Veredicto:** ✅ Núcleo operativo de Fase 3 cerrado.

---

## AUD-017 — Fase 2.5 (parte 2): alertas como incidentes con histéresis (2026-06-22)

**Fase:** Fase 2.5 — Calidad de datos (punto 1 de [AUD-015](#aud-015--revisión-crítica-tras-fase-2--productivización-brechas-y-mejores-ideas-2026-06-22), el de mayor impacto).
**Alcance:** Sustituir las alertas por-evento por un modelo de incidentes con estado.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

El modelo anterior disparaba **una alerta por cada evento** que cruzaba el umbral (un dispositivo a
96 °C reportando a 10 Hz → 10 alertas/seg de la misma condición). Ahora es un **incidente** por
`(deviceId, rule)` con máquina de estados:

- **Motor con histéresis** (`AlertRules.Read` → `RuleReading`): bandas de disparo (warning/critical)
  y de **despeje** separadas (abre a 95 °C, cierra a < 90) para evitar *flapping*; entre ambas, no
  emite señal. Puro y sin estado.
- **Máquina de estados** (`IncidentTransitions.Decide`, pura): abrir / escalar severidad / resolver.
  Solo las **transiciones** se persisten y notifican; los firing repetidos solo actualizan el valor.
- **Store `alert_incidents`** (Postgres + InMemory, interfaz `IAlertIncidentStore`): una fila por
  `(device_id, rule)`. `ApplyAsync` reduce las lecturas del lote a la última por clave, decide
  transiciones y hace upsert. Caché de **incidentes abiertos** para no consultar la BD por cada
  lote de telemetría normal (los Clear solo importan si la clave está abierta).
- **Worker/procesador**: emiten solo transiciones al canal de alertas; `/api/alerts` =
  `GetActiveAsync` (abiertos primero). **Frontend**: `AlertStore` indexa por `incidentId` (upsert por
  transición); UI con columna de estado (abierta/resuelta, filas resueltas atenuadas); el badge
  cuenta solo **críticas abiertas**.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🔴  | Alertas por-evento → inundación de ruido | Incidentes con estado + histéresis | Resuelto |
| 🟠  | Telemetría normal emite señales Clear → SELECT por lote en Postgres | Caché de incidentes abiertos filtra los Clear irrelevantes | Resuelto |
| 🟡  | La caché de abiertos es coherente en **un** proceso worker; a varias instancias haría falta particionar por dispositivo (FIFO) | Documentado (deuda, ligado a AUD-008/015) | Abierto (por diseño) |
| 🟡  | Sin ack/resolución manual ni notificación externa (solo dashboard) | Fuera de alcance; idea registrada en AUD-015 A | Abierto |

### Verificaciones

- [x] `nx test api-tests`: **29/29** (reglas con histéresis + transiciones `Decide` + integración
      InMemory abrir→resolver). `nx run-many -t build lint test` (5 proyectos) ✅
- [x] **E2E** (Aws, simulador 1.500 ev/s × 30 s = 45.000 ev, 40 disp): `alert_incidents` = **65 filas**
      (antes habrían sido miles), con **Open y Resolved** por regla (p. ej. overspeed 3 abiertas /
      22 resueltas) → la histéresis abre y resuelve según el valor cruza las bandas. `/api/alerts`
      devuelve 65 (33 abiertas primero, 32 resueltas).

### Conclusión

El alertado deja de ser un contador de cruces de umbral y pasa a un modelo de **incidentes** real:
acotado (O(dispositivos×reglas), no O(eventos)), con apertura/escalado/resolución e histéresis
anti-*flapping*. Con esto se cierra el Top 1 de AUD-015 y la **Fase 2.5 (calidad de datos)** queda
completa (puntos 1–3).

**Veredicto:** ✅ AUD-015 punto 1 cerrado. Fase 2.5 completa.

---

## AUD-016 — Fase 2.5 (parte 1): retención por DROP PARTITION + data lake S3 idempotente (2026-06-22)

**Fase:** Fase 2.5 — Calidad de datos (puntos 2 y 3 de [AUD-015](#aud-015--revisión-crítica-tras-fase-2--productivización-brechas-y-mejores-ideas-2026-06-22)).
**Alcance:** Camino frío: retención real de particiones + idempotencia del data lake.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

- **Retención O(1) cableada** (AUD-015 p2): `ITelemetryArchive.DropPartitionsBeforeAsync(cutoff)`
  (Postgres: lista `pg_inherits`, parsea la fecha del nombre y `DROP TABLE` las viejas) +
  `PartitionRetentionService` (BackgroundService en el worker): pasada al arrancar y cada
  `Retention:IntervalHours` (6 h), conserva `Retention:Days` (30) días. Convención de nombres en
  `PartitionName` (testeable). Antes el particionado existía pero **nadie dropeaba** → el beneficio
  estrella de ADR-007 no se materializaba.
- **Data lake S3 idempotente** (AUD-015 p3): la clave del objeto se deriva del **hash del
  contenido** (`RawEventKey`: `raw/yyyy/MM/dd/{sha256(body)}.json`, fecha del evento más antiguo).
  Una reentrega del mismo lote (at-least-once) sobrescribe el mismo objeto en vez de duplicar.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟠  | Retención descrita pero no ejecutándose | Job de `DROP PARTITION` en el worker | Resuelto |
| 🟠  | Data lake S3 no idempotente (dups bajo at-least-once) | Clave por hash de contenido | Resuelto |
| 🔵  | El separador `/` en un format de fecha es sensible a cultura (salía `-`) | Escapado `yyyy'/'MM'/'dd`; partición SQL ya usaba `-` literal | Resuelto |

### Verificaciones

- [x] `nx test api-tests`: **28/28** (+`RawEventKeyTests` ×3, +`PartitionNameTests` ×3). `nx run-many -t lint test build` ✅
- [x] **E2E retención**: creada `telemetry_p20200101`; al reiniciar el worker → log
      `Retención: 1 particiones eliminadas (< 05/23/2026): telemetry_p20200101`; psql confirma que
      solo queda la partición de hoy.
- [x] **E2E S3**: tras ingestar, los objetos son `raw/2026/06/22/{sha256}.json` (clave por contenido).

### Conclusión

El camino frío gana calidad de datos: la retención por particiones ya **se ejecuta** (no solo está
descrita) y el data lake deja de poder duplicar bajo reentrega. Falta el punto 1 (alertas como
incidentes), el de mayor impacto.

**Veredicto:** ✅ AUD-015 puntos 2 y 3 cerrados.

---

## AUD-015 — Revisión crítica tras Fase 2 + productivización: brechas y mejores ideas (2026-06-22)

**Tipo:** Auditoría retrospectiva (no de ejecución), homóloga de
[AUD-008](#aud-008--revisión-crítica-de-fases-01-brechas-con-producción-y-mejoras-2026-06-22).
Mira con ojo crítico lo construido **desde** AUD-008 (Fase 1.5, Fase 2, CDK, viewport):
qué se rompería en producción, qué duele al escalar, qué ideas son mejores.
**Alcance:** Estado actual completo del sistema.
**Auditor:** Fabián Rubio + Claude

### Qué cerró AUD-008 (para no repetir)

✅ Auth de ingesta (token+rate limit) · ✅ latencia P95 + OTel · ✅ reconexión sin huecos ·
✅ grupos por viewport ([AUD-014](#aud-014--productivización-grupos-por-viewport-en-signalr-2026-06-22)) ·
✅ carga k6 + consumidores en paralelo · ✅ camino frío telemetry+S3 ([AUD-012](#aud-012--fase-2-slice-2-camino-frío--telemetría-particionada--s3-data-lake-2026-06-22)) ·
✅ IaC con CDK ([AUD-013](#aud-013--productivización-infraestructura-como-código-con-aws-cdk-2026-06-22)).

### Leyenda de prioridad
🔴 Bloqueante para producción · 🟠 Importante (escala/seguridad/datos) · 🟡 Deseable · 🔵 Pulido

---

### A. Alertas — el mayor "demo vs realidad" de Fase 2

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🔴 | **Alerta por-evento, no por-incidente**: `AlertRules.Evaluate` dispara en *cada* evento que cruza el umbral; `alertId={eventId}:{rule}` hace cada una distinta | Un dispositivo a 96 °C reportando a 10 Hz genera **10 alertas/seg** de la misma condición → tabla y dashboard inundados, ruido inservible | **Modelo de incidente** con estado: clave por `(deviceId, rule)`, abre/cierra; solo las **transiciones** crean filas. Histéresis (abrir a 95, cerrar a 90) para evitar *flapping* |
| 🟠 | Sin **ack/resolución** ni notificación externa (solo push al dashboard) | "Notificadas en sub-segundos" se cumple en la UI, pero no hay operación (silenciar/resolver) ni canal (email/SNS→SMS) | Estado `acknowledged/resolved` + endpoint de ack; SNS topic de notificaciones para fan-out a email/SMS |
| 🟡 | Umbrales **hardcodeados** en `AlertRules` (constantes) | No se pueden ajustar por flota/dispositivo sin recompilar | Reglas configurables (tabla/JSON) por flota; recarga en caliente |

### B. Durabilidad e idempotencia del dato

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | **Borde de ingesta at-most-once** (efecto de [AUD-010](#aud-010--desacople-del-publish-a-sns-en-ingest-2026-06-22)): `/ingest` responde 202 desde un canal **en memoria**; un crash de la API pierde lo *buffered* (antes el publish síncrono daba garantía al request) | Se cambió durabilidad por latencia; bajo despliegue/caída se pierden eventos aceptados | **API Gateway → SNS** (service integration, sin proceso propio): durable **y** baja latencia; o WAL local antes del 202 |
| 🟠 | **Data lake S3 sin dedup**: el dedup vive en Redis (preventivo, TTL); a *at-least-once* el `AppendAsync` a S3 **no es idempotente** (cada reentrega/fallo parcial = objeto nuevo con los mismos eventos) | Reentrega tardía (post-TTL) o crash entre S3 y `DeleteMessageBatch` → **duplicados en el lake** → Athena doble-cuenta | Clave S3 derivada del **hash del contenido** (idempotente), o `SELECT DISTINCT` por `event_id` en Athena/consultas |
| 🟡 | Worker escribe device_state + alerts + telemetry + S3 + broadcast **sin transacción**; borra el mensaje al final | Un fallo a mitad reprocesa todo (idempotente salvo S3) | Aceptable con efectos idempotentes; documentar el orden y hacer S3 idempotente (arriba) |

### C. Camino frío — la retención prometida no está cableada

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | **Retención O(1) anunciada pero no implementada**: se crean particiones diarias, pero **nadie hace `DROP PARTITION`** de las viejas | La tabla `telemetry` crece sin fin; el beneficio estrella del particionado (ADR-007) no se materializa | Job programado (timer en worker / `pg_cron`) que dropea particiones > N días. El lifecycle S3 (IA→Glacier) ya quedó en el CDK |
| 🟡 | `/api/history` devuelve **crudo** (cap 5.000), sin agregación | Un rango de 30 días pide millones de puntos; el SAD habla de "promedio histórico" / continuous aggregates | **Downsampling** server-side (avg/min/max por bucket); evaluar TimescaleDB (hypertables + continuous aggregates) |

### D. Seguridad (parcial desde AUD-008)

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | **Token de ingesta único compartido** (no por dispositivo) | Un token filtrado compromete toda la flota; sin revocación granular | Token/credencial **por dispositivo** (o mTLS / SigV4); rotación |
| 🟠 | **Lecturas sin autenticación**: `/api/devices|alerts|history` y el hub son abiertos (CORS a localhost) | Cualquiera lee la telemetría de la flota | **OIDC/JWT** en la API y el hub; autorización por flota/tenant |
| 🔵 | Secretos en `appsettings` (dev) | En prod, secretos en claro | Secrets Manager/SSM; el CDK puede inyectarlos |

### E. Operación y resiliencia

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | **Sin readiness real**: `/health` no comprueba Postgres/Redis/SQS; el worker no expone health | En un orquestador no hay rolling deploy sano; arranca aunque las deps no estén | Health **gateado por dependencias** + readiness separado de liveness |
| 🟡 | **Sin graceful shutdown**: el buffer de `/ingest` y los lotes en vuelo del worker se pierden/reprocesan al SIGTERM | Despliegues pierden lo buffered (atado a B) | Cancelación cooperativa + drenar el canal antes de cerrar |
| 🟡 | **DLQ sin replay** (ya en CDK) ni backoff/circuit-breaker explícito | Mensajes envenenados se acumulan sin operación | Herramienta de replay de DLQ; backoff+jitter |

### F. Pruebas

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | **El SQL del camino frío no tiene test automatizado**: particionado, `unnest`, `ON CONFLICT`, creación perezosa de particiones solo se verificaron a mano | Una regresión en el DDL/queries no la atrapa CI (los tests usan InMemory) | **Testcontainers** (Postgres real) para `PostgresTelemetryArchive`/`PostgresAlertRepository` |
| 🟡 | Frontend: sin tests de `FleetStore`/`AlertStore`/viewport ni E2E | Lógica de coalescencia/dedup/viewport sin red de seguridad | Unit de stores (signals) + **Playwright** del flujo (ver flota → alerta → histórico) |

### G. Frontend y varios (heredado de AUD-008, sigue abierto)

| Pri | Brecha (hoy) | Idea mejor |
|-----|--------------|------------|
| 🟡 | Mapa = scatter en canvas (sin geografía) | **deck.gl / Mapbox GL** con clustering por viewport |
| 🟡 | Tabla cap 200 filas, sin virtual scroll | CDK Virtual Scroll + paginación server-side |
| 🔵 | API URL hardcodeada (`devApiConfig`); CDK single-env | Config por entorno; parametrizar stacks dev/staging/prod |

---

### Top 5 mejoras priorizadas (mayor valor / acercan a la realidad)

1. 🔴 **Alertas como incidentes** (estado abrir/cerrar + histéresis): elimina el ruido y es lo que distingue un alertado real de un contador de cruces de umbral.
2. 🟠 **Retención por `DROP PARTITION`** (job programado): materializa el beneficio estrella de ADR-007 que hoy solo está descrito.
3. 🟠 **Idempotencia del data lake S3** (clave por hash de contenido): cierra la duplicación silenciosa del camino frío bajo at-least-once.
4. 🟠 **Durabilidad del borde de ingesta** (API GW→SNS o WAL local): recupera la garantía que AUD-010 cambió por latencia, sin volver al cuello de botella.
5. 🟠 **Testcontainers** para el SQL del camino frío/alertas: el código Postgres real hoy no tiene red de seguridad en CI.

### Diferencias clave "demo vs realidad" (resumen honesto)

Tras Fase 2 + productivización, el sistema ya **mide, escala el push, persiste el histórico y
está como IaC**. Las brechas restantes no son "más features", son **calidad de datos y operación**:
las alertas son por-evento (no incidentes), el data lake puede duplicar, la retención está descrita
pero no ejecutándose, y la durabilidad del borde se cambió por latencia. Son exactamente los puntos
finos que separan una arquitectura demostrada de una operada en producción.

**Veredicto:** ✅ Base madura y honesta. Recomendación: una **Fase 2.5 de calidad de datos**
(Top 1–3) tiene más valor que nuevas features; el resto (seguridad fina, Testcontainers, deck.gl)
es endurecimiento incremental.

---

## AUD-014 — Productivización: grupos por viewport en SignalR (2026-06-22)

**Fase:** Productivización (cierra el Top de [AUD-008](#aud-008--revisión-crítica-de-fases-01-brechas-con-producción-y-mejoras-2026-06-22)).
**Alcance:** Escalar el push: el cliente recibe solo los deltas de los dispositivos visibles.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

El push pasaba **todo a todos** (`Clients.All`). Ahora soporta **grupos por viewport** (AUD-008),
**opt-in y sin regresión por defecto**:

- **`ViewportRegistry`** (singleton): por conexión, el conjunto de dispositivos seguidos.
- **`TelemetryHub`**: `SyncViewport(ids)` deja a la conexión suscrita exactamente a esos grupos
  `device:{id}` (diff add/remove); `ClearViewport()` vuelve al firehose; limpieza en
  `OnDisconnectedAsync`.
- **Envío dual** (forwarder Aws + procesador InMemory): si nadie está en viewport, un único
  `Clients.All` (comportamiento idéntico al previo). Si hay clientes viewport, se les **excluye del
  broadcast** (`Clients.AllExcept`) y se les manda **solo su grupo**; el envío por grupos recorre
  solo la **unión de dispositivos suscritos** (evita sends a grupos vacíos — fix de la revisión).
- **Frontend**: `TelemetryStreamService.setViewport(ids|null)` (re-aplica tras reconectar);
  `FleetStore.setViewport`; en el dashboard, control **Todo / 2× / 4×** que recorta el viewport al
  centro del mapa, sincroniza la membresía solo cuando cambia, y **atenúa los dispositivos
  congelados** (fuera del viewport).

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅  | Push escalable por viewport, sin romper el firehose por defecto | grupos + `AllExcept` | Resuelto |
| 🟡  | (revisión) El envío por grupos iteraba todos los dispositivos → sends a grupos vacíos | `SubscribedDevices()`: solo la unión suscrita | Resuelto |
| 🔵  | (revisión) `setViewport(null)` invocaba `ClearViewport` aun ya en firehose | guarda `wasFirehose` | Resuelto |
| 🟡  | Descubrimiento: un cliente viewport solo conoce dispositivos vía snapshot/reconexión (un disp. nuevo no aparece hasta el próximo snapshot) | Aceptable: semántica de viewport; snapshot en (re)conexión | Abierto (por diseño) |

### Verificaciones

- [x] `nx run-many -t build` (5) ✅ · `nx run-many -t lint test` ✅
- [x] `nx test api-tests`: **18/18** — incluye **`ViewportTests`**: un **cliente SignalR real**
      en modo viewport recibe el delta de `keep` y **no** el de `other` (prueba hub→grupos→registro→envío dual).
- [x] Revisión de calidad (recall) sobre el diff: 2 hallazgos, ambos corregidos.

### Conclusión

AUD-008 cerrado: el push deja de ser O(flota) por cliente y pasa a O(viewport). Por defecto nada
cambia (firehose); el modo viewport es aditivo y verificado E2E con un cliente SignalR.

**Veredicto:** ✅ Grupos por viewport listos y verificados.

---

## AUD-013 — Productivización: infraestructura como código con AWS CDK (2026-06-22)

**Fase:** Productivización (ADR-009).
**Alcance:** Definir la infra event-driven como CDK; reemplaza el script `awslocal`.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

`infra/cdk/` — proyecto **CDK standalone** (TypeScript, aislado del monorepo Angular) que define
la infra del SAD como código (ADR-009), equivalente 1:1 del `infra/localstack/init/01-resources.sh`:

- **`AtalayaStack`** (`lib/atalaya-stack.ts`): SNS topic `atalaya-telemetry` → SQS
  `atalaya-telemetry-queue` (redrive a DLQ `atalaya-telemetry-dlq`, maxReceiveCount=5) con
  suscripción `RawMessageDelivery=true`; S3 `atalaya-datalake` con **lifecycle** IA(30 d)→Glacier(90 d)
  (cold/archive, ADR-007). Nombres físicos fijos → los servicios .NET (resuelven por nombre) no cambian.
- Scripts: `synth` (offline), `deploy:local`/`destroy:local` (cdklocal contra LocalStack).
- El `docker-compose` + `01-resources.sh` se mantiene como **atajo de dev de cero fricción**; el CDK
  es la fuente de verdad productiva.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅  | Infra como código, desplegable a LocalStack o AWS real | `infra/cdk` (CDK v2) | Resuelto |
| 🔵  | `cdklocal` 2.x rompe con la CLI nueva de aws-cdk (2.1xxx): `lib/cdk-toolkit` not exported | `aws-cdk-local` 3.x ([TS-007](./TROUBLESHOOTING.md#ts-007--cdklocal-rompe-con-la-cli-nueva-de-aws-cdk-libcdk-toolkit-not-exported)) | Resuelto |
| 🟡  | El `01-resources.sh` y el CDK quedan **duplicados** (dos fuentes de la misma infra) | Aceptable: script = atajo dev; CDK = productivo. Migrar dev a cdklocal queda opcional | Abierto (por diseño) |

### Verificaciones

- [x] `cdk synth` → CloudFormation válido (bucket+lifecycle, DLQ, cola+redrive, topic, subscripción), **offline sin cuenta**.
- [x] `cdklocal bootstrap` + `cdklocal deploy` contra LocalStack standalone → stack `AtalayaStack`
      desplegado (9/9 recursos, 5,2 s). Verificado con `awslocal`: cola+DLQ, topic, bucket,
      `RedrivePolicy maxReceiveCount=5`, suscripción SNS→SQS. Contenedor de prueba eliminado.

### Conclusión

ADR-009 cumplido: la infra deja de vivir solo en un script imperativo y pasa a CDK versionado,
sintetizable y desplegable (verificado contra LocalStack). Pendiente de productivización: Athena
(AWS real) y grupos por viewport en SignalR.

**Veredicto:** ✅ IaC con CDK lista y verificada.

---

## AUD-012 — Fase 2 (slice 2): camino frío — telemetría particionada + S3 data lake (2026-06-22)

**Fase:** Fase 2 — Camino frío (segundo vertical slice del roadmap SAD §10).
**Alcance:** Tabla `telemetry` particionada por tiempo + S3 data lake + vista histórica.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

Segundo vertical de Fase 2, el **camino frío** (ADR-005/007), sin tocar el caliente:

- **Tabla `telemetry` particionada por rango de tiempo** (`PostgresTelemetryArchive`): parent
  `PARTITION BY RANGE (ts)` + particiones diarias `telemetry_pYYYYMMDD` creadas **de forma
  perezosa** (idempotente y tolerante a carreras entre consumidores). Habilita retención O(1) por
  `DROP PARTITION`. Insert por lote con `unnest` + `ON CONFLICT DO NOTHING` (idempotente). Índice
  `(device_id, ts DESC)`.
- **S3 data lake** (`S3RawEventArchive` en el worker, AWSSDK.S3): vuelca cada lote crudo como un
  objeto JSON inmutable bajo `raw/yyyy/MM/dd/`. `ForcePathStyle` para LocalStack. Bucket asegurado
  al arrancar (lo crea el init de LocalStack; `EnsureBucketAsync` es idempotente).
- **Worker**: tras el camino caliente, escribe `fresh` a SQL particionado + S3; métrica OTel
  `atalaya.telemetry.archived`.
- **API**: `GET /api/history?deviceId&minutes&limit` lee el archivo (no los read models en vivo).
  **Paridad InMemory** (`InMemoryTelemetryArchive`) para dev sin Docker y tests.
- **Frontend**: feature `history` — selector de dispositivo (autocompletado desde el read model
  en vivo) + rango + métrico, **gráfico SVG** de la serie temporal + tabla + resumen (mín/máx/prom).
  Consulta REST puntual; no usa el stream.
- **Athena**: documentado como **solo AWS real** (LocalStack community no lo trae); la vista local
  se sirve desde Postgres particionado. Deuda registrada, no bloqueante.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅  | Telemetría cruda persiste en SQL particionado + S3, consultable por histórico | ITelemetryArchive + IRawEventArchive | Resuelto |
| 🟡  | Athena (consulta ad-hoc del data lake) no corre en LocalStack community | Documentado como solo-AWS-real; histórico local sobre Postgres | Abierto (por diseño) |
| 🔵  | Particiones diarias creadas perezosamente: una carrera entre 2 consumidores podría duplicar el CREATE | `EnsurePartitionAsync` ignora 42P07/23505/23P01 | Resuelto |

### Verificaciones

- [x] `nx run-many -t build` (5 proyectos) ✅ · `nx run-many -t lint test` ✅
- [x] `nx test api-tests`: **17/17** (+2 de integración InMemory de `/api/history`).
- [x] **E2E** (API+worker en Aws, simulador 500 ev/s × 10 s = 4.950 ev): tabla `telemetry`
      **4.950 filas / 20 dispositivos** (cero pérdida), partición `telemetry_p20260622` creada,
      **10 objetos** crudos en `s3://atalaya-datalake/raw/2026-06-22/`, métrica
      `atalaya.telemetry.archived=4.950`, `GET /api/history` devuelve 248 puntos de `dev-00001`
      ordenados por ts desc.

### Conclusión

Camino frío cerrado: la telemetría cruda viaja a SQL particionado por tiempo (retención O(1)) y al
data lake S3 inmutable, y la vista histórica la consulta sin competir con el camino caliente. Con
esto **Fase 2 queda completa** (alertas + camino frío). Pendiente de productivización: Athena sobre
S3 (AWS real) e IaC con CDK (ADR-009).

**Veredicto:** ✅ Fase 2 completa (alertas + camino frío).

---

## AUD-011 — Fase 2 (slice 1): alertas por umbral end-to-end (2026-06-22)

**Fase:** Fase 2 — Alertas (primer vertical slice del roadmap SAD §10).
**Alcance:** Reglas por umbral → read model `alerts` → notificación en vivo → UI de alertas.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

Vertical slice completo del NFR estrella ("alertas en sub-segundos"), reusando el camino
caliente ya endurecido (ADR-001/002/005/006):

- **Motor de reglas** (`AlertRules` en contracts, puro): umbrales de temperatura de motor,
  combustible y velocidad, cada uno con nivel aviso/crítico. `AlertId` determinista
  (`{eventId}:{rule}`) para idempotencia (ADR-006).
- **Read model `alerts`** (Postgres, `PostgresAlertRepository`): inserción por lote con
  `unnest` + `ON CONFLICT (alert_id) DO NOTHING RETURNING` → idempotente y devuelve solo las
  alertas nuevas (las que hay que notificar). Snapshot `GetRecentAsync`.
- **Worker**: tras dedup, evalúa reglas sobre eventos frescos → inserta → publica las nuevas a
  un canal Redis propio (`RedisAlertBroadcaster`, `atalaya:alerts:new`) → métrica OTel
  `atalaya.alerts.raised`.
- **API**: `RedisAlertForwarder` reenvía Redis→SignalR (`alertsRaised`, sin coalescencia: bajo
  volumen, máxima prontitud); endpoint `GET /api/alerts`. **Paridad InMemory** (`IAlertStore`)
  para dev sin Docker y tests: el `TelemetryProcessor` dispara las mismas reglas.
- **Frontend**: `AlertStore` (signals, fuera del NgRx Store — ADR-003; dedup por `alertId`,
  snapshot al (re)conectar + vivo), feature `alerts` (tabla en vivo + conteos por severidad),
  badge de críticas en la nav. `AlertSeverity` serializa como string en todo el cableado.

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅  | Alertas E2E sobre el camino caliente real | reglas → `alerts` → SignalR → UI | Resuelto |
| 🔵  | El stub del shell no proveía `AlertStore` (nuevo inject) → test del shell roto | Stub añadido en `app.spec.ts` | Resuelto |
| 🟡  | El simulador no genera valores en rango crítico (solo aviso); lo crítico solo se ve por unit test | Aceptable; los umbrales crít. están cubiertos en `AlertRulesTests` | Abierto (cosmético) |

### Verificaciones

- [x] `nx run-many -t build` (5 proyectos) ✅ · `nx run-many -t lint test` ✅
- [x] `nx test api-tests`: **15/15** (incluye 9 de `AlertRules` + 1 de integración InMemory de `/api/alerts`).
- [x] **E2E** (API+worker en Aws, simulador 1.000 ev/s × 10 s): worker `atalaya.alerts.raised`
      poblado, `RedisAlertForwarder` activo, `GET /api/alerts` devuelve alertas con reglas/severidad
      correctas (engine-temp-high / fuel-low / overspeed).

### Conclusión

Primer slice de Fase 2 cerrado: las alertas viajan del evento al dashboard por el mismo pipeline
desacoplado e idempotente del camino caliente, con read model propio y notificación en vivo.
Falta el segundo vertical (camino frío: `telemetry` particionada + S3 data lake + vista histórica).

**Veredicto:** ✅ Fase 2 — alertas completas y verificadas.

---

## AUD-010 — Desacople del publish a SNS en `/ingest` (2026-06-22)

**Fase:** Cierre del hallazgo 🔴 de [AUD-009](#aud-009--fase-15-endurecimiento--hallazgo-de-prueba-de-carga-2026-06-22) (remedio #2).
**Alcance:** Camino de ingesta de la API en modo Aws: `/ingest` → SNS.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

El cuello medido en AUD-009 era el **`PublishAsync` síncrono a SNS por request**: la API
esperaba el round-trip a SNS dentro del handler, así que bajo concurrencia los requests se
encolaban y la latencia explotaba (p95 **34 s**).

Se **desacopló el publish del request** (remedio #2 de AUD-009):
- `/ingest` ahora solo **encola** los eventos en un canal en memoria acotado y responde **202**
  al instante (`QueueingTelemetryPublisher`, sustituye al `SnsTelemetryPublisher` síncrono).
- Un `SnsBatchPublisher` (BackgroundService) **drena** el canal, **coalesce** una ventana corta
  (25 ms) y publica a SNS por **lotes**: agrupa eventos en mensajes (cada uno un array JSON, como
  ya esperaba el worker) y manda hasta **10 mensajes por `PublishBatch`** (límite de SNS). Pasa de
  un PublishAsync por request a unas pocas llamadas batch por segundo.
- El armado de lotes (`PlanBatches`, método puro y testeado) respeta **los dos** límites de SNS
  PublishBatch: ≤10 mensajes **y ≤256 KB por lote** (con margen, presupuesto 240 KB). Esto cierra
  un riesgo latente de pérdida silenciosa: a 100 ev/msg, 10 mensajes rozaban los 256 KB.
- Canal **acotado con espera** (backpressure): si el publicador se atrasa, la escritura espera en
  vez de perder eventos (consistente con el `InMemoryTelemetryBus`, ADR-001).
- Tunables en `Aws` (`AwsOptions`): `PublisherQueueCapacity` (200k), `MessageMaxEvents` (100),
  `FlushMilliseconds` (25).

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| ✅  | `/ingest` ya no bloquea en SNS | Encolar + batch en background | Resuelto |
| 🟠  | El `PublishBatch` rozaba el límite de 256 KB/lote (100 ev/msg × 10 ≈ 250 KB): overflow = rechazo + pérdida silenciosa | `PlanBatches` acota el lote por bytes (presupuesto 240 KB) además de por ≤10 entradas; test unitario fija el contrato | Resuelto |
| 🟡  | Un `PublishBatch` fallido **pierde** esos eventos (ya no se propaga al request); igual al apagar la API se pierde lo buffered (canal no durable) | Se loguea como error; reintento/persistencia/drenado en shutdown queda como deuda | Abierto |
| 🔵  | `HotPathTests` fallaba en HEAD: `WebApplicationFactory` arranca en *Development* y exigía el token de dev | El test fuerza `Ingest:Token=""` (cubre el camino caliente, no la auth) | Resuelto |

### Verificaciones

- [x] `nx build api` · `nx test api-tests` ✅ (5 tests: camino caliente + 4 de `PlanBatches`) · `nx run-many -t lint test` ✅
- [x] **E2E** (API+worker en Aws, simulador 2.000 ev/s × 30 s): `enviados=59.200 fallidos=0`,
      worker `events.processed=59.200` (cero pérdida), cola SQS = 0.
- [x] **k6** (ramp a ~5.000 ev/s, 65 s): **p95 = 34 ms** (umbral <500 ms ✅), `http_req_failed=0%`,
      2.212 requests todos 202, cola SQS = 0 (el publicador mantuvo el ritmo).

### Conclusión

El cuello de ingesta de AUD-009 queda resuelto en la capa de la API: **p95 de `/ingest` baja de
~34 s a ~34 ms** (≈1000×) sosteniendo el objetivo de 5.000 ev/s contra LocalStack, con cero
pérdida E2E. La meta a escala real sigue dependiendo de medir contra AWS real y, si hace falta,
ingesta serverless (remedio #3, sin urgencia). Deuda menor: reintento/persistencia ante
`PublishBatch` fallido.

**Veredicto:** ✅ Hallazgo crítico de AUD-009 cerrado.

---

## AUD-009 — Fase 1.5 (endurecimiento) + hallazgo de prueba de carga (2026-06-22)

**Fase:** Fase 1.5 — Endurecimiento (Top 5 de [AUD-008](#aud-008--revisión-crítica-de-fases-01-brechas-con-producción-y-mejoras-2026-06-22)).
**Alcance:** Reconexión, medición de latencia, push endurecido, auth de ingesta, carga k6.
**Auditor:** Fabián Rubio + Claude

### Qué se hizo

| # | Mejora | Estado | Verificación |
|---|--------|--------|--------------|
| 1.5.1 | **Reconexión sin huecos**: re-snapshot en cada (re)conexión | ✅ | lint/test/build |
| 1.5.2 | **Latencia evento→pantalla**: P50/P95 en el dashboard + OTel en worker (histograma + contadores) | ✅ | processed=17.850, histograma poblado |
| 1.5.3 | **Push endurecido**: await + coalescencia server-side 50 ms + backpressure (canal acotado) | ✅ | 7.800 ev → 21 mensajes / 1.680 deltas |
| 1.5.4 | **Auth de ingesta**: token `X-Ingest-Token` + rate limiting (429) | ✅ | sin token 401 · con token 202 |
| 1.5.5 | **Carga k6 + consumidores en paralelo** (worker) | ✅ harness; ⚠️ ver hallazgo | ver abajo |

### 🔴 Hallazgo de la prueba de carga (el más importante)

**Objetivo:** sostener 5.000 ev/seg. **Resultado contra LocalStack:** NO se alcanza.
- `/ingest` colapsa bajo concurrencia (300 VUs): `http_req_duration` avg **14 s**, p95 **34 s**,
  ~**13 req/s** efectivos, 938 iteraciones descartadas (techo ~1.000–1.300 ev/s).
- **Cola SQS = 0** y el worker (2 consumidores) drenó todo → **el consumo NO es el cuello
  de botella**.
- **Causa raíz:** la API hace un **`PublishAsync` a SNS por request y lo espera**; contra
  **LocalStack** ese publish serializa y es lento, así que los requests se encolan.

**Lecturas / remedios (orden de impacto):**
1. **LocalStack no representa el throughput real** de SNS/SQS — es una herramienta de dev.
   La cifra de 5.000 ev/s debe medirse contra AWS real (o un stub de alto rendimiento).
2. **Desacoplar el publish del request**: `/ingest` encola en un buffer local y responde
   202 al instante; un publicador en segundo plano hace **batch** a SNS (`PublishBatch`) o
   **SQS `SendMessageBatch`**. Baja la latencia de `/ingest` drásticamente.
3. **Ingesta serverless** (SAD §4.1): API Gateway + Lambda, o **Kinesis Data Firehose**
   para volúmenes muy altos, en vez de una API monolítica publicando 1 a 1.
4. El **simulador (Node, `setInterval`)** tampoco es un generador fiable a 5k; k6 es mejor,
   pero el límite aquí fue el servidor, no el generador.

### Conclusión

Las cuatro mejoras de correctitud/seguridad/observabilidad quedaron hechas y verificadas.
La quinta entregó su verdadero valor: **una medición honesta que revela el techo real**
(~1k ev/s contra LocalStack) y señala dónde está (publish síncrono a SNS), no en el
consumo. Esto es exactamente la clase de dificultad que aparece "en la realidad" y queda
documentada con remedios concretos.

**Veredicto:** ✅ Fase 1.5 completa. La meta de 5.000 ev/s pasa a depender de (2)/(3) y de
medir contra AWS real — registrado como deuda priorizada.

---

## AUD-008 — Revisión crítica de Fases 0–1: brechas con producción y mejoras (2026-06-22)

**Tipo:** Auditoría retrospectiva (no de ejecución). Mira lo construido con ojo crítico:
qué se rompería en el mundo real, qué duele al escalar y qué ideas mejores acercan el
sistema a producción. Las entradas AUD-001…007 registran *qué* se hizo; esta registra
*qué le falta y cómo mejorarlo*.
**Alcance:** Todo lo implementado hasta hoy (scaffold, camino caliente sobre infra real).
**Auditor:** Fabián Rubio + Claude

### Leyenda de prioridad
🔴 Bloqueante para producción · 🟠 Importante (escala/seguridad) · 🟡 Deseable · 🔵 Pulido

---

### A. Ingesta y seguridad

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🔴 | **`/ingest` sin autenticación**: cualquiera puede inyectar telemetría | Dispositivos falsos, envenenamiento de datos, DoS | Tokens de ingesta por dispositivo + rate limiting; usuarios con OIDC/JWT (SAD §8) |
| 🟠 | La **API hace REST + ingesta + SignalR** en el mismo proceso | La ingesta compite con las lecturas/push; no escalan por separado | Servicio/Lambda de ingesta dedicado tras API Gateway (como en el SAD §4.1); la API solo lectura+hub |
| 🟠 | Ingesta publica a **SNS standard** (sin orden, sin FIFO) | Eventos desordenados entre mensajes; sólo el guard `seq` salva el read model | SQS **FIFO** (o partición por `deviceId`) donde el orden importe (alertas, telemetría cruda) |
| 🟡 | Payload sin validación de esquema ni límites de tamaño/lote | Mensajes gigantes o malformados degradan el worker | Validación + límite de tamaño en el borde; rechazo temprano |

### B. Procesamiento y escalado (worker)

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | **Un solo consumidor**, bucle secuencial de SQS | No sostiene 5.000 ev/s; sin autoescalado por profundidad de cola (ADR-008) | Varios consumidores/instancias; recepción en paralelo; autoescalar por `ApproximateNumberOfMessages` |
| 🟠 | **Dedup en Redis preventivo**: el upsert ya es idempotente; el dedup sólo importará para efectos no idempotentes (S3, disparo de alertas) que aún no existen | TTL 1h: una reentrega tras 1h (DLQ replay, visibility) se reprocesaría | Atar TTL a la ventana real de reentrega; aplicar dedup donde haya efectos no idempotentes |
| 🟡 | Upsert abre conexión por ciclo (pooling de Npgsql implícito) | Bajo carga, picos de latencia si el pool no está afinado | Afinar pool; considerar `COPY`/Timescale para volumen; medir |
| 🔵 | Sin *graceful shutdown* que drene en vuelo | Al reiniciar, mensajes en proceso vuelven a la cola (ok) pero sin orden de drenado | Cancelación cooperativa + drenado al recibir SIGTERM |

### C. Tiempo real (SignalR)

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | **Broadcast a `Clients.All`**: cada cliente recibe TODA la flota, aunque el hub ya tiene `Subscribe/Unsubscribe` por grupo (sin usar) | Con cientos de dispositivos y muchos clientes, ancho de banda y render desperdiciados (ADR-002) | Suscripción por **grupo de dispositivos visibles** (viewport); el worker/forwarder publica por grupo |
| 🟠 | **Puente Redis pub/sub** en vez del **backplane nativo** de SignalR | Funciona, pero reimplementa lo que SignalR ya resuelve (grupos, escalado) | `AddSignalR().AddStackExchangeRedis(...)` y `IHubContext` desde el worker |
| 🔴 | **Sin relleno de gaps en reconexión** (ADR-006): el cliente sólo pide snapshot al inicio; al reconectar no re-sincroniza | Tras un corte, el dashboard queda con datos viejos/huecos | En reconexión: re-pedir snapshot y/o enviar último `seq` y que el server reenvíe desde un buffer corto |
| 🟡 | Forwarder hace `SendAsync` *fire-and-forget* sin await ni manejo de error | Bajo carga, concurrencia sin control y errores silenciosos | Await + coalescencia/throttle por cliente en el server |

### D. Datos y camino frío

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | Sólo existe el read model `device_state`; **no hay telemetría cruda ni S3** (ADR-007) | Sin histórico ni fuente de verdad fría; no se puede reproyectar | Tabla `telemetry` particionada por tiempo + escritura de crudos a S3 (Fase 2) |
| 🟡 | Sin catálogo `devices`, sin tenant/fleet, sin índices más allá del PK | Multi-cliente y consultas por flota se complican luego | Modelar `devices`/`fleets`; índices por (device_id, ts); retención por *drop* de partición |

### E. Observabilidad, resiliencia y pruebas

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🔴 | **El NFR estrella (latencia evento→pantalla P95) no se mide** | "Tiempo real" sin número es una afirmación, no un hecho | **OpenTelemetry** end-to-end con el `seq` como correlación; dashboard de latencia y profundidad de cola |
| 🟠 | **Sin prueba de carga**: nunca se validó 5.000 ev/s; el simulador (Node, `setInterval`) tope ~1.800/2.000 por *drift* de timer | No sabemos el punto de quiebre real | **k6** o generador multi-hilo; medir pérdida y P95 bajo carga sostenida |
| 🟠 | DLQ existe pero **sin herramienta de replay**; sin retry/backoff explícito ni circuit breaker | Mensajes envenenados se acumulan sin operación clara | Replay de DLQ; backoff+jitter; circuit breaker en dependencias |
| 🟡 | Cobertura mínima: 1 test de integración (InMemory) + 2 unit | Regresiones difíciles de atrapar | **Marble tests** de la coalescencia (SAD §10); integración contra LocalStack (Testcontainers); E2E (Playwright) |
| 🟡 | Sin health/liveness en api/worker; arranque falla si Postgres/Redis no están | En orquestadores, sin readiness no hay rolling deploy sano | Endpoints de health + readiness gateada por dependencias |

### F. Frontend

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟡 | Mapa = scatter en canvas por lat/lng (sin geografía real) | No es un mapa usable para operación | **deck.gl / Mapbox GL** con clustering y render del viewport (SAD §9) |
| 🟡 | Tabla de dispositivos sin **CDK Virtual Scroll** (cap a 200 filas) | Con miles de filas, el DOM se hace pesado | CDK Virtual Scroll; servidor pagina/filtra |
| 🔵 | URL de API hardcodeada en `devApiConfig` | No sirve para varios entornos | Externalizar por entorno (build-time o `/config`) |

### G. Infra y operación

| Pri | Brecha (hoy) | Dificultad real | Idea mejor |
|-----|--------------|-----------------|------------|
| 🟠 | Recursos con **`awslocal`**, no **CDK** (ADR-009) | La infra no es versionada/reproducible en la nube | Definir SNS/SQS/S3/Redis/RDS con **AWS CDK**; `cdklocal` en dev |
| 🟡 | Credenciales/strings en `appsettings` (dev) | En prod, secretos en claro = riesgo | Secrets Manager/SSM; config por entorno |
| 🔵 | CI con `nx affected` incluye .NET por `run-commands`, **sin verificar en runner** | El CI podría no estar realmente verde | Ejecutar el workflow en un PR de prueba y confirmar |

---

### Top 5 mejoras priorizadas (mayor valor / acercan a la realidad)

1. 🔴 **Medir el NFR estrella**: OTel + latencia evento→pantalla y profundidad de cola. Sin esto, "tiempo real" no está demostrado.
2. 🔴 **Reconexión sin huecos** (ADR-006) en el cliente: re-snapshot + replay por `seq`. Es correctitud, no lujo.
3. 🟠 **Grupos de SignalR por viewport** + backplane nativo: lo que separa una demo de algo que escala.
4. 🟠 **Prueba de carga real (k6)** a 5.000 ev/s con consumidores en paralelo: descubre el punto de quiebre antes que el cliente.
5. 🔴 **Auth de ingesta** + separar el servicio de ingesta: seguridad y escalado independiente.

### Diferencias clave "demo vs realidad" (resumen honesto)

Lo construido **demuestra la arquitectura** y corre el flujo de extremo a extremo, pero
hoy es **single-instance, sin auth, sin medición de latencia, con push broadcast y sin
camino frío**. La distancia a producción no está en "más features", sino en
**seguridad, medición, escalado horizontal y resiliencia operativa**. Varias de estas ya
estaban en el roadmap (Fase 2/3); esta auditoría las hace explícitas y prioriza.

**Veredicto:** ✅ Base sólida y honesta; con un plan claro de endurecimiento. Recomendación:
intercalar una **Fase 1.5 de endurecimiento** (puntos 1–5) antes o en paralelo a la Fase 2.

---

## AUD-007 — Rendimiento del frontend: change detection zoneless (2026-06-22)

**Fase:** Fase 1 — afinado de rendimiento del dashboard
**Alcance:** Eliminar el jank bajo carga pasando a change detection sin zone.js (ADR-010).
**Auditor:** Fabián Rubio + Claude

### Hallazgos

| Sev | Hallazgo | Acción | Estado |
|-----|----------|--------|--------|
| 🟠 | UI con micro-tirones bajo carga: con **zone.js**, cada mensaje SignalR dispara un ciclo de CD, aunque coalescemos cada 100 ms (el problema del firehose, ADR-010) | Activar zoneless | Resuelto |
| ✅ | `provideZonelessChangeDetection()` en el bootstrap; `zone.js` fuera de polyfills | — | OK |
| ✅ | El render lo gobiernan los **signals**: el caudal de eventos ya no provoca CD, solo el `set` coalescido | — | OK |
| ✅ | Build prod OK; tests verdes (manejan CD vía `detectChanges`) | — | OK |
| 🔵 | Parte del tirón también venía del **modo dev** (`nx serve` sin optimizar + backend en Debug); un build prod va más fluido | Medir con build prod | Abierto |

### Conclusión

El dashboard pasa a **zoneless**, cumpliendo el pendiente del ADR-010. Combinado con
OnPush + Signals + coalescencia, el firehose deja de acoplar la llegada de eventos al
change detection. Queda como nota que el `nx serve` de desarrollo no representa el
rendimiento real (medir con build de producción).

**Veredicto:** ✅ Mejora de rendimiento aplicada y verificada (build/test).

---

## AUD-006 — Cableado del pipeline real: SNS/SQS + Redis + Postgres + SignalR (2026-06-22)

**Fase:** Fase 1 — Camino caliente sobre infraestructura real (sustituye los shims)
**Alcance:** 4 pasos incrementales conectando los servicios .NET a la infra Docker.
**Auditor:** Fabián Rubio + Claude

### Hallazgos

| Sev | Hallazgo | Estado |
|-----|----------|--------|
| ✅ | **Paso 1** — `/ingest` publica el lote a **SNS** (un mensaje); worker consume **SQS** (long-poll, redrive→DLQ). Flag `Telemetry:Transport` (InMemory\|Aws) preserva el test sin Docker | OK |
| ✅ | **Paso 2** — `libs/persistence` (Dapper/Npgsql): worker hace **upsert** de `device_state` (unnest, guard seq); `GET /api/devices` lee de **Postgres** | OK |
| ✅ | **Paso 3** — `libs/realtime`: **dedup en Redis** (SET NX EX, pipeline) en el worker; verificado con lote duplicado (aplicados=2, duplicados=2) | OK |
| ✅ | **Paso 4** — worker publica deltas a **Redis pub/sub**; la API los reenvía por **SignalR** (`RedisDeltaForwarder`). Dashboard en vivo por el pipeline real | OK |
| ✅ | **E2E del lazo completo**: simulador→API→SNS→SQS→worker→dedup→Postgres→Redis→API→SignalR→cliente = 4.680 eventos/60 disp, **cero pérdida** | OK |
| 🔵 | Push usa puente Redis pub/sub, no el backplane nativo de SignalR (AddStackExchangeRedis) | Abierto (equivalente; productivizar) |
| 🔵 | Falta escritura de crudos a **S3 data lake** y tabla `telemetry` particionada (ADR-007) | Abierto (Fase 2) |
| 🔵 | Infra con `awslocal`, no **CDK** (ADR-009) | Abierto |

### Verificaciones

- [x] Build solución 0 errores; `nx test api-tests` (InMemory) verde.
- [x] Paso 1: worker procesó 3.900/3.900.
- [x] Paso 2: `/api/devices`=30 desde Postgres; `psql` confirma filas.
- [x] Paso 3: dedup filtró el lote repetido; claves `dedup:*` en Redis.
- [x] Paso 4: cliente SignalR recibió 4.680/4.680 vía pipeline real.

### Conclusión

El camino caliente ya **no usa shims en memoria**: corre sobre SNS/SQS, Postgres y Redis,
con dedup idempotente y push en vivo, todo reproducible en local con Docker. Cada paso se
hizo incremental, verificado y commiteado. Lo aislado tras interfaces
(`ITelemetryPublisher`, `IDeviceStateRepository`, `IEventDeduplicator`,
`ITelemetryBroadcaster`) permitió el cambio sin reescritura del dominio.

**Veredicto:** ✅ Fase 1 sobre infraestructura real, completa y verificada E2E.

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
