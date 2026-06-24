# Software Architecture Document (SAD)
## Atalaya — Plataforma de monitoreo y alertamiento de flota/IoT en tiempo real

| Campo | Valor |
|---|---|
| Proyecto | Atalaya — telemetría, monitoreo y alertas en tiempo real (Angular + .NET + AWS) |
| Versión | 1.0.0 |
| Estado | Approved for implementation |
| Autor | Fabián Rubio — Full Stack (foco Frontend / Angular) |
| Audiencia | Equipo de ingeniería, Product, evaluadores técnicos |
| Última revisión | 2026-06-18 |

> **Nota de lectura.** Este SAD describe *por qué* el sistema está construido así, no solo *qué* contiene. Las decisiones se registran como ADRs con su contexto y trade-offs. **Atalaya** es la torre de vigía: ingiere telemetría de miles de dispositivos, mantiene proyecciones en vivo y dispara alertas en sub-segundos. El nombre es la metáfora rectora; la arquitectura de capas (cada dependencia tras una interfaz, ADR-011) permite sustituir el proveedor cloud. La app es deliberadamente *full-stack con foco frontend*: el backend existe para que el frontend en tiempo real tenga algo real que estresar.
>
> **⚠️ Pivote de nube (2026-06-23, [ADR-013](#adr-013--pivote-de-nube-aws--gcp-mismo-diseño-otro-proveedor)).** El target cloud pasó de **AWS** a **Google Cloud (GCP)**: la narrativa del portafolio y el despliegue real serán en GCP (Pub/Sub, Cloud Storage, BigQuery, Identity Platform, Cloud Run, Cloud SQL, Memorystore), con emuladores locales para desarrollar sin coste. Los ADR-001…012 conservan su valor de diseño (event-driven, CQRS, idempotencia, SignalR, auth flag-gated); ADR-013 reemplaza únicamente la **elección de proveedor** y su mapeo de servicios. Las menciones a AWS/SNS/SQS/S3/Athena/Cognito/CDK más abajo son el estado *previo* (hoy implementado contra LocalStack) y se leen junto a la tabla de equivalencias del ADR-013.

---

## 1. Contexto y objetivos

### 1.1 Problema que resuelve
Operar una flota (vehículos, maquinaria, sensores IoT) exige saber **ahora** dónde está cada activo, en qué estado, y reaccionar a anomalías antes de que escalen. El reto técnico no es mostrar un dato: es sostener un **flujo de alto volumen** (miles de eventos/seg) de extremo a extremo —ingesta, procesamiento, almacenamiento, push al navegador— sin que ninguna capa se caiga ni la UI se congele, y diferenciar el **camino caliente** (lo que pasa ahora, sub-segundo) del **camino frío** (histórico sobre millones de filas).

### 1.2 Objetivos (qué consideramos éxito)
- **Tiempo real verificable:** latencia evento→pantalla P95 < 1.5 s bajo carga sostenida.
- **Alto volumen sostenido:** ingerir ≥ 5.000 eventos/seg sin pérdida ni degradación de la UI.
- **Confiabilidad del pipeline:** entrega *at-least-once* con deduplicación; cero eventos perdidos ante caídas de un consumidor.
- **UI que no se ahoga:** el dashboard sigue fluido (60 fps objetivo) con cientos de dispositivos activos en pantalla.
- **Reacción:** alertas por umbral disparadas y notificadas en sub-segundos desde el evento que las origina.

### 1.3 Fuera de alcance
- Dispositivos físicos reales: se usa un **simulador de telemetría** (generador de carga configurable) como fuente. Es deliberado: demuestra el manejo de volumen y permite reproducir picos.
- App móvil nativa: el cliente es una SPA Angular responsive.
- Machine learning de detección de anomalías: las alertas son por reglas/umbral. Se deja el punto de extensión documentado, no implementado.

---

## 2. Drivers de arquitectura y atributos de calidad

Prioridades ordenadas. Cuando dos chocan, gana el de más arriba.

| # | Atributo | Por qué prioriza | Cómo se mide |
|---|---|---|---|
| 1 | **Latencia / Tiempo real** | Es la razón de existir del producto | latencia evento→pantalla P50/P95 |
| 2 | **Escalabilidad (alto volumen)** | El diferenciador frente a un CRUD | throughput sostenido sin pérdida; profundidad de cola |
| 3 | **Confiabilidad / Resiliencia** | Telemetría perdida = decisiones ciegas | tasa de pérdida; recuperación tras fallo de consumidor |
| 4 | **Rendimiento del frontend** | Una UI que se congela invalida el tiempo real | fps bajo carga; tiempo de change detection |
| 5 | **Observabilidad** | Sin métricas, el pipeline es una caja negra | cobertura de trazas/métricas RED del pipeline |
| 6 | **Seguridad** | Datos de operación + autenticación de dispositivos | authz por rol; auth de ingesta |

**Decisión consciente:** Atalaya optimiza para *throughput y latencia*, no para consistencia transaccional fuerte. La telemetría tolera *eventual consistency* en las proyecciones; lo que no tolera es perder eventos ni bloquear la ingesta.

---

## 3. Restricciones y supuestos

**Restricciones**
- Stack objetivo: **Angular** (frontend, foco), **TypeScript**, **RxJS**, **gestión de estado (NgRx)**, **.NET** en backend, **SQL relacional**, **cloud orientada a eventos**. La nube es **GCP** desde el pivote ([ADR-013](#adr-013--pivote-de-nube-aws--gcp-mismo-diseño-otro-proveedor)); la fase previa se construyó contra AWS/LocalStack.
- El frontend es el foco de evaluación: la profundidad va en Angular/RxJS/estado; el backend es sólido pero acotado.

**Supuestos**
- Fuente de datos vía simulador HTTP/MQTT-like; payloads JSON de telemetría (id, timestamp, posición, métricas).
- **GCP** como nube ([ADR-013](#adr-013--pivote-de-nube-aws--gcp-mismo-diseño-otro-proveedor)); el pipeline se reproduce localmente con **emuladores** (Pub/Sub emulator, fake-gcs-server) para desarrollar sin coste. La fase AWS usaba **LocalStack**.
- Auth basada en JWT/OIDC para usuarios (en GCP: **Identity Platform**); las "credenciales de dispositivo" se modelan con tokens de ingesta.

---

## 4. Vista general de la arquitectura

### 4.1 Contexto del sistema (camino caliente y frío)
```
                         ┌──────────────────────────────────────────────┐
 Simulador / dispositivos │                                              │
   (miles de eventos/seg) │        ┌────────────┐   SNS    ┌─────────┐   │
        │ HTTP/ingest      └──────► │ Ingestion   │ ───────► │  SQS    │   │
        ▼                           │ (API GW +   │  (fan-   │ (buffer)│   │
   ┌─────────┐                      │  Lambda)    │   out)   └────┬────┘   │
   │  Auth   │                      └────────────┘                │        │
   └─────────┘                                                    ▼        │
                                        ┌───────────────────────────────┐  │
   CAMINO CALIENTE (sub-segundo)        │  Workers .NET (consumidores)  │  │
   ┌──────────────┐   SignalR    ┌──────┤  - dedup (idempotencia)       │  │
   │ Angular SPA  │ ◄─────────── │ API  │  - reglas/alertas             │  │
   │ (dashboard)  │   WebSocket   │ .NET │  - actualiza read models      │  │
   └──────────────┘              └──┬───┘  - escribe telemetría         │  │
          ▲                         │      └─────────────┬─────────────┘  │
          │ REST (histórico)        │                    │                │
          │                         ▼                    ▼                │
   CAMINO FRÍO              ┌──────────────┐     ┌──────────────┐         │
   (agregaciones)          │ SQL (hot):   │     │ S3 (data lake)│         │
                           │ read models +│     │ eventos crudos│         │
                           │ telemetría   │     │ inmutables    │         │
                           │ particionada │     │ (cold/archive)│         │
                           └──────────────┘     └──────────────┘         │
                                                                          │
                           ElastiCache/Redis: dedup set + SignalR backplane
                          └──────────────────────────────────────────────┘
```

### 4.2 La espina dorsal orientada a eventos
La ingesta **nunca escribe directo a la base**. Publica a **SNS** (fan-out), que encola en **SQS** (buffer que absorbe los picos). Workers .NET consumen, deduplican, aplican reglas y actualizan **read models** + escriben telemetría cruda a SQL y al **data lake S3**. Esto desacopla la velocidad de ingesta de la velocidad de procesamiento: si un pico llega, se acumula en la cola, no tumba el sistema (ADR-001).

### 4.3 Separación camino caliente / camino frío (CQRS pragmático)
- **Caliente:** el dashboard lee **read models** precalculados (última posición por dispositivo, conteo de alertas activas) y recibe deltas por **SignalR**. Lecturas baratas, push en vivo.
- **Frío:** consultas históricas/agregadas corren sobre telemetría particionada por tiempo y, para rangos grandes, sobre S3 vía Athena. Nunca compiten con el camino caliente (ADR-005).

### 4.4 Arquitectura del frontend (Angular)
- **Standalone components** + lazy routes por feature (mapa, dispositivos, alertas, históricos).
- **Smart/dumb:** contenedores conectan estado y streams; los de presentación son `OnPush` y reciben datos por input/signal.
- **Estado en dos planos (clave, ADR-003):** NgRx Store para el **estado de aplicación** (filtros, sesión, selección, catálogos, agregados); el **firehose en vivo** *no* entra al Store global — vive en una capa de streams RxJS + Component Store, y solo se materializa lo que la vista necesita.
- **RxJS** como sistema nervioso: streams de SignalR → operadores de coalescencia/throttle → render. Higiene de suscripciones con `takeUntilDestroyed` y `async pipe` (ADR-004).

---

## 5. Decisiones de arquitectura (ADRs)

### ADR-001 — Ingesta desacoplada por eventos (SNS → SQS), no escritura directa
**Contexto:** miles de eventos/seg con picos; escribir directo a SQL acopla ingesta a la velocidad de la base y la tumba en un pico.
**Decisión:** la ingesta publica a SNS (fan-out a múltiples suscriptores) y SQS amortigua; los workers consumen a su ritmo.
**Razón:** la cola es el amortiguador. Desacopla productores de consumidores, permite escalar consumidores por profundidad de cola y sobrevivir picos sin pérdida.
**Trade-off:** complejidad operativa (colas, DLQ, idempotencia) y *eventual consistency*. Aceptado: es el corazón del requisito "event-driven + alto volumen" de la vacante.

### ADR-002 — SignalR para push en tiempo real (sobre WebSocket), no polling ni SSE
**Contexto:** el dashboard necesita deltas en vivo con baja latencia y reconexión robusta.
**Decisión:** **SignalR** (WebSocket con fallback) desde .NET hacia Angular, con **backplane Redis** para escalar entre instancias.
**Razón:** SignalR resuelve reconexión, grupos (suscribir solo a los dispositivos visibles) y backplane out-of-the-box; encaja con .NET. SSE es unidireccional y sin grupos; polling no escala a sub-segundo.
**Trade-off:** dependencia de SignalR y manejo de gaps en reconexión (ver ADR-006). Aceptado.

### ADR-003 — El firehose en vivo no entra al NgRx Store global
**Contexto:** despachar una acción por evento (miles/seg) inunda el Store, los selectors y Redux DevTools, y dispara change detection en cascada.
**Decisión:** NgRx Store guarda **estado de aplicación y agregados**; el stream de alta frecuencia se maneja en una capa RxJS dedicada + **Component Store** local a la feature, con `@ngrx/entity` para colecciones y coalescencia antes de tocar el estado.
**Razón:** el Store global es para estado que se comparte y se depura, no para un caudal de telemetría. Mantenerlo fuera evita el cuello de botella clásico de NgRx en tiempo real.
**Trade-off:** dos planos de estado a entender. Se documenta la regla: "¿lo comparten varias features o se depura? → Store. ¿Es caudal efímero de una vista? → stream/Component Store."

### ADR-004 — Gestión de suscripciones RxJS disciplinada (async pipe + takeUntilDestroyed)
**Contexto:** las fugas por suscripciones no cerradas son el bug nº1 de Angular en tiempo real.
**Decisión:** preferir `async pipe` en el template; donde haya `subscribe` imperativo, `takeUntilDestroyed`. Prohibido `subscribe` anidado (usar `switchMap`/`mergeMap`/`concatMap` según semántica).
**Razón:** elimina fugas de memoria y suscripciones zombi por diseño, no por revisión manual.
**Trade-off:** disciplina; se valida con lint (`rxjs/no-nested-subscribe`, `rxjs-angular`).

### ADR-005 — Camino caliente vs frío con read models (CQRS pragmático)
**Contexto:** la consulta "última posición de 5.000 dispositivos" y la consulta "promedio histórico de 30 días" tienen perfiles opuestos; mezclarlas hace ambas lentas.
**Decisión:** los workers mantienen **read models** desnormalizados para el camino caliente; el histórico vive en tablas particionadas por tiempo + S3.
**Razón:** el dashboard lee proyecciones baratas en vez de agregar eventos crudos en cada request. Lecturas predecibles bajo carga.
**Trade-off:** los read models hay que mantenerlos (más lógica en los workers) y son *eventually consistent*. Aceptado para telemetría.

### ADR-006 — Entrega at-least-once + idempotencia + relleno de gaps
**Contexto:** SQS entrega *at-least-once* (duplicados posibles) y sin orden global garantizado; SignalR puede perder mensajes durante una desconexión.
**Decisión:** **deduplicación** por clave de evento (set en ElastiCache/Redis + constraint único en SQL); los eventos con orden estricto usan **SQS FIFO**, el resto **standard** por throughput. En reconexión, el cliente envía el último `seq` visto y el server **reenvía** desde un buffer corto.
**Razón:** garantiza "procesado exactamente una vez" a nivel de efecto, y que el cliente no quede con huecos tras un corte.
**Trade-off:** estado de dedup (TTL en Redis) y un buffer de replay. Costo justificado por la integridad.

### ADR-007 — Almacenamiento: SQL particionado para lo caliente, S3 como data lake para lo frío
**Contexto:** escribir miles de filas/seg y consultar millones rompe una tabla monolítica.
**Decisión:** telemetría reciente en SQL **particionada por tiempo** (PostgreSQL; se evalúa TimescaleDB para hypertables + continuous aggregates); eventos crudos inmutables a **S3** con lifecycle (cold/archive). Athena para consultas históricas ad-hoc.
**Razón:** mantiene el requisito "SQL relacional" para lo que importa transaccionalmente y descarga el volumen frío a almacenamiento barato y escalable. Particionar permite *drop* de particiones viejas como retención O(1).
**Trade-off:** dos almacenes y una política de retención que mantener. Aceptado.

### ADR-008 — Backend .NET: Minimal API + workers como servicios separados
**Contexto:** la API de lectura/real-time y el procesamiento de cola tienen perfiles de escalado distintos.
**Decisión:** **Minimal API** (.NET) para REST + hub SignalR; **workers** (.NET Worker Service / consumidores SQS) como procesos aparte que escalan por profundidad de cola.
**Razón:** separar API de procesamiento permite escalar cada uno independiente; minimal API reduce ceremonia.
**Trade-off:** más unidades de despliegue. Lo resuelve IaC (ADR-009).

### ADR-009 — Infraestructura como código (AWS CDK) + LocalStack en dev
**Contexto:** un sistema event-driven es irreproducible sin su infra; el dev local no puede depender de la nube real.
**Decisión:** **AWS CDK** (tipado, afín a .NET/TS) define SNS/SQS/Lambda/S3/ElastiCache; **LocalStack** levanta el pipeline localmente.
**Razón:** infra versionada y reproducible; los devs corren el pipeline completo sin AWS. Esto es lo que separa una demo de una plataforma.
**Trade-off:** curva de CDK y paridad imperfecta de LocalStack. Aceptado.

### ADR-010 — Frontend de alto rendimiento: OnPush + Signals + coalescencia
**Contexto:** miles de updates/seg disparan change detection en cascada y congelan la UI.
**Decisión:** `ChangeDetectionStrategy.OnPush` en todo; **Signals** para estado de vista con `toSignal` sobre los streams; **coalescer** el firehose (`bufferTime` + render por lote en `animationFrameScheduler`); evaluar **zoneless** change detection.
**Razón:** se renderiza por lotes a ritmo de frame, no por evento. Es la diferencia entre un dashboard fluido y uno trabado.
**Trade-off:** disciplina de inmutabilidad y de `trackBy`. Documentado.

### ADR-011 — Implementación incremental: shims de dev tras interfaces, sustituibles por la infra real
**Contexto:** construir el camino caliente de extremo a extremo antes de tener toda la nube exige poder correr el sistema en cada etapa, no solo al final.
**Decisión:** cada dependencia de infraestructura se define tras una **interfaz** (`ITelemetryPublisher`, `IDeviceStateRepository`, `IEventDeduplicator`, `ITelemetryBroadcaster`) con dos implementaciones: una *in-memory* (modo `InMemory`, sin Docker, para tests y arranque rápido) y una real (modo `Aws`: SNS/SQS, Postgres, Redis). Un flag `Telemetry:Transport` selecciona el modo. Dos atajos de dev conscientes respecto al objetivo del SAD: (a) el push en vivo usa un **puente Redis pub/sub** (worker→Redis→API→SignalR) en lugar del **backplane nativo de SignalR** (`AddStackExchangeRedis`); (b) los recursos AWS se crean con **`awslocal`** en LocalStack en lugar de **AWS CDK** (ADR-009).
**Razón:** entregar valor verificable por incrementos sin acoplar el dominio a un proveedor; el test de integración corre sin Docker; el cambio a la infra real no reescribe la lógica.
**Trade-off:** dos implementaciones que mantener y dos atajos a productivizar (backplane nativo + CDK). Aceptado y registrado como deuda explícita en [AUDIT.md](./AUDIT.md) (AUD-006).

---

### ADR-012 — Auth de lecturas: JWT Bearer flag-gated (Dev HS256 / Oidc JWKS) + RBAC operador/admin
**Contexto:** las lecturas (`/api/devices|alerts|history` y el hub SignalR) estaban abiertas (solo CORS a localhost): cualquiera podía leer la telemetría de la flota (riesgo 🟠 de [AUD-015](./AUDIT.md)). El objetivo es OIDC/JWT por usuario (§8), pero una cuenta AWS/Cognito real está hoy bloqueada y el sistema debe seguir corriendo en local en cada etapa (mismo principio que ADR-011).
**Decisión:** autenticación **JWT Bearer** seleccionada por un flag `Auth:Mode` con tres modos: `Disabled` (base/tests, sin auth, como el token de ingesta vacío), `Dev` (la API **emite y valida** tokens **HS256** con una clave simétrica local vía `/auth/dev-token`, sin depender de un IdP) y `Oidc` (valida la firma contra el **JWKS** de un `Authority` real — Cognito — cuando la cuenta esté lista). El swap Dev→Oidc es solo configuración: **no toca el código de los endpoints**. Autorización **RBAC** por claim de rol `role`: política `read` (operador o admin) en todas las lecturas REST y en el hub; política `admin` reservada para acciones futuras. El WebSocket recibe el token por `?access_token=` (`JwtBearerEvents.OnMessageReceived`), que es lo que entrega el `accessTokenFactory` del cliente. La **ingesta no cambia**: `/ingest` sigue gobernada por el token de dispositivo (`X-Ingest-Token`), no por la auth de usuario; `/health/*` quedan libres.
**Razón:** cierra la brecha de lecturas abiertas (el item de mayor peso del backlog) demostrando la cadena de auth de extremo a extremo sin acoplarse a un proveedor ni exigir nube; el frontend (interceptor + `accessTokenFactory` + auto-token al arrancar) queda intacto al pasar a OIDC real.
**Trade-off:** el modo Dev guarda una clave simétrica en `appsettings.Development` (en claro, aceptable solo en dev) y usa auto-token sin login real ni refresh-token; en prod = modo `Oidc` (JWKS rotable) + secretos en SSM/Secrets Manager. Registrado en [AUDIT.md](./AUDIT.md) (AUD-019).

---

### ADR-013 — Pivote de nube AWS → GCP: mismo diseño, otro proveedor
**Contexto:** la fase de portafolio se construyó sobre AWS (SNS/SQS/S3/Athena/Cognito/CDK), reproducida en local con LocalStack. Para **desplegar de verdad** (no solo emulado) se dispone de una cuenta **Google Cloud** con presupuesto (~US$200). Desplegar en una nube real pesa más en el portafolio que "probado contra LocalStack", y GCP cubre cada pieza con servicios gestionados. La arquitectura ya está desacoplada del proveedor (ADR-011: cada dependencia tras una interfaz), así que el cambio no toca el dominio.
**Decisión:** **pivotar el target cloud a GCP**. La narrativa pasa a *"Angular/.NET/GCP event-driven con despliegue real"*. Se mantiene intacta la filosofía de ADR-011: las implementaciones GCP entran **tras las interfaces existentes** (`ITelemetryPublisher`, `IRawEventArchive`, …), seleccionadas por el flag de transporte (nuevo valor `Gcp` junto a `Aws`/`InMemory`), y el desarrollo usa **emuladores locales** para no gastar (Pub/Sub emulator, `fake-gcs-server`), reservando la nube real para validar/demostrar. Mapeo de servicios:

| Pieza (AWS, fase previa) | GCP (target) | Emulador local |
|---|---|---|
| SNS → SQS (ingesta, ADR-001) | **Pub/Sub** (topic + subscriptions) | Pub/Sub emulator |
| S3 data lake (camino frío, ADR-007) | **Cloud Storage (GCS)** | `fake-gcs-server` |
| Athena (analítica sobre el lake) | **BigQuery** (external table NDJSON, G4 ✅ AUD-024) | — (sin emulador; se valida en la nube) |
| Cognito / OIDC (ADR-012) | **Identity Platform** (JWKS) | emisor dev HS256 (ya existe) |
| CDK (IaC, ADR-009) | **Terraform / OpenTofu** | `terraform plan` local |
| EC2/ECS (cómputo) | **Cloud Run** (API + worker en contenedor) | `dotnet run` / Docker local |
| (Redis) | **Memorystore** | Redis en Docker |
| (Postgres) | **Cloud SQL for PostgreSQL** | Postgres en Docker |
| Hosting SPA | **Firebase Hosting** | `nx serve` local |

**Razón:** entregar el sistema **desplegado en una nube real** sin reescribir el dominio, demostrando que el desacoplamiento por interfaces (ADR-011) funciona de verdad ante un cambio de proveedor. GCP además aporta BigQuery (cierra Athena gratis a baja escala) e Identity Platform (cierra el OIDC real, swap por config del modo `Oidc`).
**Trade-off:** (a) la narrativa deja de coincidir *literal* con vacantes "AWS" — se asume a favor de demostrar despliegue real y portabilidad; (b) no es swap por config: SNS/SQS/S3 estaban atados al **AWS SDK**, así que hay que **escribir adaptadores GCP** (Pub/Sub publisher + consumidor, archivo GCS) — trabajo real, aunque acotado por las interfaces; (c) **costo**: Pub/Sub/Cloud Run/BigQuery escalan a cero, pero Cloud SQL/Memorystore cobran por hora ociosos → se exige **Budget+Alert** y teardown por Terraform. Roadmap de ejecución por fases G0…G6 en [AUDIT.md](./AUDIT.md) (AUD-020). La fase AWS no se borra: queda como historia de diseño y como prueba del swap de proveedor.

---

## 6. Modelo de datos y almacenamiento

- **`devices`** — catálogo (id, tipo, metadata). Relacional clásico.
- **`telemetry`** — eventos (device_id, ts, lat/lng, métricas...). **Particionada por tiempo** (rango diario/semanal). Índice por (device_id, ts).
- **`device_state`** (read model) — última posición/estado por dispositivo. Una fila por dispositivo, actualizada por el worker. Es lo que lee el camino caliente.
- **`alerts`** — alertas disparadas (device_id, regla, severidad, ts, estado). Read model de conteos para el dashboard.
- **S3 data lake** — partición `s3://.../raw/yyyy/mm/dd/` con eventos crudos inmutables. Fuente de verdad fría y base de Athena.

**Retención:** particiones SQL se *dropean* pasado el ventana caliente (ej. 30 días) — retención O(1), sin DELETE masivos. El histórico completo permanece en S3.

---

## 7. Flujo de datos en tiempo real (end-to-end)

1. Dispositivo/simulador → **Ingestion** (API GW + Lambda), autentica y valida.
2. Publica a **SNS** → fan-out a **SQS** (y opcionalmente a una cola de analítica).
3. **Worker .NET** consume el lote: dedup (Redis), aplica reglas (¿alerta?), actualiza `device_state`/`alerts`, escribe `telemetry` y emite el crudo a S3.
4. El worker publica el **delta** al **hub SignalR**; el backplane Redis lo distribuye a la instancia que tiene la conexión del cliente.
5. El cliente está suscrito al **grupo** de los dispositivos visibles (no a todo). Recibe el delta con un `seq`.
6. En el navegador: el stream entra a la capa RxJS → **coalescencia** (`bufferTime`) → actualización del Component Store / signal → render `OnPush` por lote.
7. Ante reconexión, el cliente envía su último `seq`; el server reenvía desde el buffer corto (ADR-006). Sin huecos.

---

## 8. Resiliencia, seguridad y observabilidad

**Resiliencia**
- **DLQ** por cola para mensajes envenenados, con herramienta de *replay*.
- **Retry con backoff exponencial + jitter** en consumidores; **circuit breaker** en llamadas a dependencias.
- **Backpressure:** SQS es el amortiguador; los consumidores autoescalan por profundidad de cola. En el cliente, el throttle evita que un caudal excesivo ahogue el render.
- **Idempotencia** (ADR-006) para que los reintentos no dupliquen efectos.

**Seguridad**
- Usuarios: **OIDC/JWT** con refresh tokens; autorización por rol (operador/admin) en API y en filtros del hub.
- Ingesta: tokens de dispositivo; validación de payload; rate limiting en API GW.
- Secretos en AWS Secrets Manager / SSM; tráfico TLS; principio de mínimo privilegio en IAM (definido en CDK).

**Observabilidad**
- **OpenTelemetry** end-to-end: una traza sigue el evento desde ingesta → cola → worker → push, con el `seq` como correlación.
- Métricas **RED** (Rate, Errors, Duration) por servicio y **profundidad de cola** como señal de salud temprana.
- Logs estructurados; dashboards de latencia evento→pantalla (el NFR estrella) y de pérdida.

---

## 9. Performance y escalabilidad (NFRs, medidos)

| Métrica | Objetivo |
|---|---|
| Latencia evento→pantalla (P95) | < 1.5 s bajo carga sostenida |
| Throughput de ingesta sostenido | ≥ 5.000 eventos/seg sin pérdida |
| FPS del dashboard con 500 dispositivos activos | ~60 fps (sin jank perceptible) |
| Tiempo de recuperación tras caída de un worker | sin pérdida (cola retiene); reanuda al reescalar |
| Bundle inicial Angular (gzip) | < 250 KB (lazy por feature) |

**Tácticas frontend:** `OnPush` + Signals + coalescencia (ADR-010); **CDK Virtual Scroll** en listas/tablas de dispositivos; **mapa en canvas/WebGL** (Mapbox GL / deck.gl) con **clustering** y render solo del viewport, marcadores actualizados por lote y no uno a uno; `trackBy` en todo `*ngFor`.
**Tácticas backend:** procesamiento por **lotes** desde SQS; escrituras *batched* a SQL; read models desnormalizados; autoescalado por profundidad de cola.

---

## 10. Estrategia de testing

| Nivel | Herramienta | Qué cubre |
|---|---|---|
| Unit | Jest/Vitest + Jasmine | servicios, reducers/effects NgRx, mappers, reglas de alerta (.NET: xUnit) |
| RxJS | **Marble testing** (`TestScheduler`) | operadores de coalescencia, throttle, switchMap — la lógica reactiva crítica |
| Componentes | Angular Testing Library | smart/dumb, estados loading/empty/error, OnPush |
| Integración pipeline | Testcontainers + LocalStack | ingesta → cola → worker → DB, incluida dedup y DLQ |
| E2E | Cypress/Playwright | flujos: ver flota en vivo, recibir alerta, filtrar, consultar histórico |
| Carga | k6 / generador propio | sostener 5.000 ev/seg y medir latencia/pérdida |

**Reglas de oro:** la lógica RxJS se testea con marbles, no con timeouts; el pipeline se testea de extremo a extremo contra LocalStack, no con mocks que esconden la realidad del at-least-once; la prueba de carga es parte del *Definition of Done* del camino caliente, no un extra.

---

## 11. CI/CD e infraestructura

```
install → lint (incl. reglas RxJS) → typecheck → unit (front + .NET) → marble tests
        → build front + back → test integración (LocalStack) → E2E
        → deploy infra (CDK diff/deploy) → smoke + carga ligera
```
- **Monorepo** (Nx) para Angular + .NET + IaC, con caché de tareas afectadas.
- **CDK** despliega la infra; entornos efímeros por PR donde sea viable.
- Versionado semántico de la API; contratos validados (OpenAPI) entre front y back.

---

## 12. Riesgos y mitigaciones

| Riesgo | Impacto | Mitigación |
|---|---|---|
| El firehose congela la UI (change detection) | Alto | OnPush + Signals + coalescencia por frame (ADR-010); virtual scroll; mapa WebGL |
| NgRx inundado por eventos de alta frecuencia | Alto | Firehose fuera del Store global (ADR-003); agregados sí, caudal no |
| Fugas de memoria por suscripciones RxJS | Alto | async pipe + takeUntilDestroyed; lint que prohíbe subscribe anidado (ADR-004) |
| Duplicados / pérdida por at-least-once | Alto | Dedup idempotente + FIFO donde importa + replay de gaps (ADR-006) |
| Tabla de telemetría inmanejable por volumen | Medio | Particionado por tiempo + S3 data lake + retención O(1) (ADR-007) |
| Pipeline irreproducible en local | Medio | IaC con CDK + LocalStack (ADR-009) |
| Cold starts / costo de Lambda en picos | Medio | Reserved/provisioned concurrency donde aplique; medir costo/perf |
| SignalR no escala entre instancias | Medio | Backplane Redis + suscripción por grupos visibles (ADR-002) |

---

## 13. Roadmap por fases

**Fase 0 — Cimientos (semana 1).** Monorepo Nx, esqueleto Angular (standalone + routing lazy), Minimal API .NET, CDK base (SNS/SQS/S3/Redis) + LocalStack, simulador de telemetría mínimo, CI esqueleto.

**Fase 1 — Camino caliente (semanas 2–3).** Ingesta → SNS/SQS → worker con dedup → `device_state` → SignalR → dashboard con mapa en vivo. NgRx para estado de app; capa de streams + coalescencia para el caudal. Aquí se prueba el corazón del producto.

**Fase 2 — Alertas + camino frío (semanas 3–4).** Reglas de alerta + read model de alertas + notificación en vivo; telemetría particionada; vistas históricas; S3 data lake + Athena.

**Fase 3 — Endurecimiento (semana 5).** DLQ + replay, observabilidad OTel, prueba de carga (5.000 ev/seg), virtual scroll y tuning del mapa, seguridad (OIDC/roles), documentación y runbooks.

Cada fase entrega algo demostrable y medido. La prueba de carga y la latencia evento→pantalla se miden desde la Fase 1, no al final.

---

## 14. Glosario rápido
- **CQRS:** Command Query Responsibility Segregation — separar el modelo de escritura del de lectura.
- **Read model / proyección:** vista desnormalizada precalculada para lecturas baratas.
- **Camino caliente / frío:** datos en vivo (sub-segundo) vs históricos/agregados.
- **At-least-once:** garantía de entrega que admite duplicados (exige idempotencia).
- **DLQ:** Dead Letter Queue — cola de mensajes que no se pudieron procesar.
- **Backpressure:** mecanismo para que un productor rápido no ahogue a un consumidor lento.
- **Coalescencia:** agrupar muchos updates en uno antes de renderizar.
- **RED / USE:** familias de métricas de servicio (Rate-Errors-Duration / Utilization-Saturation-Errors).

---

## 15. Historial de revisiones

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0.0 | 2026-06-18 | Baseline. Arquitectura event-driven (SNS/SQS), camino caliente/frío con read models (CQRS pragmático), SignalR + backplane, NgRx para estado de app con el firehose fuera del Store, RxJS disciplinado, OnPush+Signals+coalescencia, almacenamiento particionado + S3 data lake, IaC con CDK + LocalStack, resiliencia (dedup/DLQ/replay), observabilidad OTel, NFRs medidos, riesgos y roadmap. ADR-001…010. |
| 1.0.1 | 2026-06-22 | ADR-011: implementación incremental con shims de dev tras interfaces (flag `Telemetry:Transport`), puente Redis pub/sub como interino del backplane SignalR y `awslocal` como interino de CDK. Refleja Fase 0–1 implementadas (ver AUDIT AUD-001…006). |
| 1.0.2 | 2026-06-23 | ADR-012: auth de lecturas con JWT Bearer flag-gated (`Auth:Mode` Dev HS256 / Oidc JWKS, OIDC-ready hacia Cognito) + RBAC operador/admin en `/api/*` y el hub (token por `?access_token=`). Cierra la brecha de lecturas abiertas de AUD-015 (ver AUDIT AUD-019). |
| 1.0.3 | 2026-06-23 | ADR-013: **pivote de nube AWS → GCP** (Pub/Sub, Cloud Storage, BigQuery, Identity Platform, Cloud Run, Cloud SQL, Memorystore; emuladores locales). Mismo diseño (ADR-011), otro proveedor; adaptadores GCP tras las interfaces. Roadmap G0…G6 en AUDIT AUD-020. Decisión registrada; implementación pendiente. |
| 1.0.4 | 2026-06-24 | Avance de ejecución de ADR-013: **G1** Pub/Sub (AUD-021), **G2** Cloud Storage data lake en NDJSON (AUD-022/024), **G3** Identity Platform auth OIDC real (AUD-023), **G4** **BigQuery** external table sobre el lake + endpoint `/api/analytics/devices` con cost guard `MaximumBytesBilled` (AUD-024). Verificados E2E (G3/G4 contra `fabian-portafolio` real). Cierra Athena. Siguiente: G5 (Terraform + Cloud Run + Firebase Hosting). |

---

*Fin del documento. SAD vivo: cada decisión futura se agrega como un ADR nuevo con su contexto y trade-offs. Una arquitectura no se documenta una vez; se mantiene.*
