# RUNBOOKS.md — Operación de Atalaya

Guías de **qué hacer cuando algo pasa en operación** (no errores de desarrollo — eso vive en
[TROUBLESHOOTING.md](./TROUBLESHOOTING.md)). Cada runbook: **síntoma → diagnóstico → acción**. Cierra la
madurez operativa de Fase 3 (readiness gateada, resiliencia at-least-once, retención O(1), teardown).

Referencia rápida de superficies de operación:

| Qué | Dónde |
|---|---|
| Liveness API | `GET :3000/health/live` (200 si el proceso vive; no mira dependencias) |
| Readiness API | `GET :3000/health/ready` (200/503 según Postgres + Redis + broker) |
| Liveness/Readiness worker | `:3100/health/live` · `:3100/health/ready` (`Health:Port`; en Cloud Run `$PORT`) |
| Replay de la DLQ | `POST :3000/api/admin/dlq/replay?max=N` (modo Gcp, RBAC **admin**) |
| Transporte | `Telemetry:Transport` = `Aws` · `Gcp` · `InMemory` |
| Auth | `Auth:Mode` = `Disabled` · `Dev` · `Oidc` |
| Retención frío | sección `Retention` (`Days`=30, `IntervalHours`=6) en el worker |

---

## RB-01 — El dashboard no recibe telemetría en vivo

**Síntoma:** mapa/tabla sin actualizarse; indicador "sin conexión"; `/api/devices` vacío o estancado.

**Diagnóstico**
1. `curl :3000/health/ready` → si **503**, una dependencia está caída (mira el cuerpo: `postgres`/`redis`/broker).
2. `curl :3100/health/ready` (worker) → ¿el worker consume? Si 503, su broker no es alcanzable.
3. ¿Hay ingesta? `POST /ingest` debe responder **202**. Revisa el simulador/origen y el token (`X-Ingest-Token`).
4. ¿El backplane vive? El push usa Redis pub/sub → SignalR (ADR-002). Si Redis cayó, no hay deltas en vivo.

**Acción**
- Restaura la dependencia caída (ver RB-02). Al volver, el `FleetStore` **re-sincroniza el snapshot**
  en cada reconexión (cierra huecos sin replay por evento, AUD-009).
- Si el broker (SNS/SQS o Pub/Sub) está saturado, escala consumidores (`Aws:Consumers` / instancias del worker).

---

## RB-02 — Redis / Postgres / broker caído

**Síntoma:** `/health/ready` = **503**; en arranque, el proceso puede fallar rápido (la conexión es fail-fast).

**Diagnóstico**
- Local: `docker ps` (deben estar `healthy`). Nube: estado de Memorystore / Cloud SQL / Pub/Sub en la consola.
- El borde de ingesta (`/ingest`) sigue aceptando 202 mientras el publicador tenga buffer; el procesamiento
  se reanuda al volver la dependencia.

**Acción**
- **Redis (Memorystore):** reinicia/espera; sin él no hay dedup ni push en vivo, pero **no se pierden**
  eventos (siguen en el broker hasta procesarse). En local tras reiniciar Docker, ver [TS-008](./TROUBLESHOOTING.md)
  (forzar IPv4 `127.0.0.1`).
- **Postgres (Cloud SQL):** los read models y el camino frío dependen de él; el broker retiene los mensajes
  (at-least-once) → al volver, el worker drena sin pérdida.
- **Broker:** si la suscripción acumula backlog, añade consumidores. Mensajes que fallan repetidamente van a
  la **DLQ** (ver RB-03).

---

## RB-03 — Mensajes en la DLQ (envenenados / fallos persistentes)

**Síntoma:** métrica `atalaya.events.poison` sube, o backlog en `atalaya-telemetry-dlq` (5 intentos agotados).

**Diagnóstico**
- **Veneno** (no deserializa): se descarta con `Ack` + log + métrica (reintentarlo es un bucle infinito).
- **Transitorio** (BD/Redis caído al procesar): `Nack` → reintenta; tras `MaxDeliveryAttempts` (5) cae a la DLQ.
- Causa común de DLQ llena: una dependencia estuvo caída más que la ventana de reintentos.

**Acción (replay, ADR-006 / AUD-027)**
1. Resuelve primero la causa (RB-02), si no el replay vuelve a fallar.
2. Re-encola desde la DLQ al topic principal:
   ```
   curl -X POST ":3000/api/admin/dlq/replay?max=1000"   # devuelve {"replayed": N}
   ```
   (modo Gcp; requiere rol **admin**). Repite si `replayed` llega al tope. El reproceso es **idempotente**
   (dedup por EventId, clave por hash, máquina de incidentes) → re-encolar de más es inocuo.
3. Para veneno real (datos corruptos), no hay replay útil: corrige el origen.

---

## RB-04 — Retención del histórico (disco del SQL creciendo)

**Síntoma:** la tabla `telemetry` crece sin parar; disco de Cloud SQL al alza.

**Diagnóstico**
- La retención corre en el **worker** (`PartitionRetentionService`): conserva `Retention:Days` (30) y dropea
  las particiones diarias anteriores con `DROP PARTITION` (O(1), no `DELETE`). Hace una pasada al arrancar y
  cada `Retention:IntervalHours` (6).
- Si el worker está caído, no hay retención → el SQL crece.

**Acción**
- Asegura que el worker corre. Para forzar una pasada, reinícialo (corre la limpieza al arrancar).
- Ajusta la ventana con `Retention__Days` / `Retention__IntervalHours` si hace falta más/menos retención.
- El crudo completo permanece en el **data lake** (GCS/S3) → dropear particiones no pierde histórico frío.

---

## RB-05 — Despliegue y apagado ordenado

**Diagnóstico/contexto**
- **Graceful shutdown:** al apagar, el publicador de ingesta (`SnsBatchPublisher` / `GcpPubSubBatchPublisher`)
  **drena su buffer** antes de salir; un lote a medio consumir no se `Ack`-ea → reentrega (at-least-once).
- **Durabilidad del borde:** el `202` de `/ingest` es previo a la durabilidad (best-effort, AUD-010); a escala
  real el diseño objetivo es API GW→broker directo.

**Acción (deploy GCP, G5b)**
- Sigue el runbook de [infra/terraform/README.md](./infra/terraform/README.md): build+push de imágenes →
  `terraform apply` → `firebase deploy` → smoke (RB-01 invertido: ingesta → mapa) → verificar `/health/ready`.
- Rollback: re-desplegar la imagen anterior en Cloud Run (revisión previa) o `terraform apply` con el tag viejo.

---

## RB-06 — Control de costo y teardown (GCP)

**Síntoma:** gasto inesperado; recursos encendidos sin uso.

**Diagnóstico/acción**
- **Cobran ociosos:** Cloud SQL, Memorystore, Cloud Run con `min_instances=1`. Escalan a cero: Pub/Sub, GCS,
  BigQuery (free-tier), Artifact Registry, Secret Manager.
- **BigQuery** es pay-per-byte: el endpoint `/api/analytics` tiene cost guard (`Gcp:AnalyticsMaxBytesBilled`,
  1 GB) porque la external table escanea todo el lake (sin poda de particiones).
- **Teardown:** `cd infra/terraform && terraform destroy`. El bucket del lake y el dataset están protegidos
  (`force_destroy=false`, `deletion_protection`) — bórralos a conciencia si quieres limpiar también los datos.
- Mantén **Budget + Alert** activo siempre que haya recursos aplicados.

---

> Operación viva: cuando un incidente real enseñe algo nuevo, añade o ajusta un runbook aquí.
