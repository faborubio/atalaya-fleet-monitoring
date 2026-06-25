# DEMO.md — Despliegue de portafolio (demo sin factura sorpresa)

> Plan de implementación **concreto** para exponer Atalaya a reclutadores sin riesgo de costo.
> Decisión registrada en [ADR-014](./SAD-Atalaya.md#adr-014--estrategia-de-demo-de-portafolio-dos-niveles). Estado del
> despliegue real previo (G5b): [AUD-031](./AUDIT.md). Runbook de la infra completa: [infra/terraform/README.md](./infra/terraform/README.md).
>
> **✅ Nivel 1 DESPLEGADO Y EN VIVO (2026-06-25):** SPA **https://atalaya-demo.web.app** · API demo
> **https://atalaya-demo-api-aqeprs2exa-uc.a.run.app** (Cloud Run scale-to-zero, ~$0). IaC en
> `infra/terraform-demo/`. **Nivel 2:** pendiente (opcional). Operación del Nivel 1 en §1.7.

## 0. Resumen y por qué

El problema: el pipeline "real" (Pub/Sub + Cloud SQL + Memorystore + 2× Cloud Run always-on) cuesta
**~US$70–120/mes encendido** porque **Cloud SQL y Memorystore no escalan a cero** (cobran por hora
ociosos). No se puede tener 24/7 barato.

La solución es **dos niveles** que se apoyan en una **base persistente casi gratis**:

| Nivel | Qué muestra | Costo | Cuándo |
|---|---|---|---|
| **Nivel 1 — Demo InMemory always-on** | El producto en vivo (mapa, alertas, histórico) en un solo Cloud Run InMemory | **~$0** (free tier; scale-to-zero) | 24/7, link público |
| **Nivel 2 — Stack efímero completo** | La infraestructura real (Pub/Sub, DLQ, Cloud SQL, autoescalado) en la consola de GCP | **Solo lo que uses** (~US$0.50–2/hora) | Entrevistas en vivo; `up` antes, `down` después |
| **Base persistente** | — (soporte de ambos) | **centavos/mes** | Siempre |

**Base persistente** (nunca se destruye; costo despreciable): Artifact Registry + imágenes, bucket del
data lake `gs://atalaya-datalake`, dataset/tabla BigQuery, APIs habilitadas, Service Accounts, secrets,
topics de Pub/Sub (gratis en reposo). El **teardown del Nivel 2 NUNCA toca la base** (al revés del
`destroy` total de G5b, que sí borró el Artifact Registry).

**Hallazgo que habilita el Nivel 1** (verificado en código, sesión 2026-06-25): el modo
`Telemetry:Transport=InMemory` ya ejecuta **todo el camino caliente en un solo proceso y empuja en vivo
por SignalR** — `TelemetryProcessor` hace `hub.Clients.All.SendAsync("devicesUpdated", …)` y `"alertsRaised"`,
archiva en `InMemoryTelemetryArchive` (el histórico funciona) y respeta el envío por viewport. **No hace
falta escribir ningún broadcaster.** Lo único que falta para el Nivel 1 es una **fuente de datos
server-side** (no hay simulador local en una demo desatendida).

---

## 1. Nivel 1 — Demo InMemory always-on (~$0)

### 1.1. Arquitectura

```
Reclutador → Firebase Hosting (SPA, gratis)
                 │  WebSocket + REST
                 ▼
         Cloud Run "atalaya-demo-api"  (min-instances=0, scale-to-zero)
           env: Telemetry__Transport=InMemory · Auth__Mode=Dev · Demo__Enabled=true
           ├─ DemoTelemetryGenerator (BackgroundService)  ← NUEVO
           │     genera telemetría sintética → ITelemetryPublisher
           └─ pipeline InMemory existente → SignalR push en vivo
         (sin Cloud SQL, sin Redis, sin Pub/Sub → sin costo por hora)
```

**Auto-regulación a $0:** en Cloud Run con `cpu_idle=true` (default) el `BackgroundService` solo recibe
CPU **mientras hay un request en vuelo**. El WebSocket del dashboard ES ese request → el generador
**solo produce datos mientras alguien mira**, y al cerrar la pestaña el instance se duerme. Cero tráfico = cero costo.

### 1.2. Código nuevo — generador de datos

**Archivo nuevo:** `apps/api/Processing/DemoTelemetryGenerator.cs`

```csharp
// BackgroundService que, con Demo:Enabled=true, inyecta telemetría sintética por el MISMO
// ITelemetryPublisher que /ingest → reusa todo el pipeline InMemory (bus → processor → SignalR).
// No corre en tests (Demo:Enabled default false). Solo tiene sentido con Transport=InMemory.
public sealed class DemoTelemetryGenerator(
    ITelemetryPublisher publisher, DemoOptions options, ILogger<DemoTelemetryGenerator> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // VISTOSO (decisión): N dispositivos agrupados en una zona reconocible (options.Lat/Lng).
        // Cada tick: movimiento fluido (random-walk suave de posición + heading coherente con la
        // dirección) + variación de speed/fuel/engineTemp; y CADA options.AlertEveryNTicks sube
        // engineTempC/speed de algún dispositivo por encima del umbral → dispara una alerta visible.
        var devices = SeedDevices(options.Devices, options.Lat, options.Lng);
        var seq = 0L;
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.IntervalMs));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var batch = devices.Select(d => d.NextEvent(++seq)).ToList(); // TelemetryEvent[]
            await publisher.PublishAsync(batch, ct);
        }
    }
}
```

**Config** (`DemoOptions`): `Enabled` (bool, default false), `Devices` (int, ej. 40), `IntervalMs`
(int, ej. 1000), `Lat`/`Lng` (centro del cluster para que se vea bien en deck.gl), opcional `AlertEveryNTicks`.

**Registro en `apps/api/Program.cs`** (tras el bloque de transporte, gateado por config):
```csharp
var demo = builder.Configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
if (demo.Enabled)
{
    builder.Services.AddSingleton(demo);
    builder.Services.AddHostedService<DemoTelemetryGenerator>();
}
```

> ⚠️ El generador depende de `ITelemetryPublisher`, que existe en los 3 modos. En `InMemory` empuja al
> bus in-proc (lo que queremos). En `Aws`/`Gcp` publicaría al broker real — por eso `Demo:Enabled` solo
> se activa en el servicio de demo (InMemory). Tests intactos (flag default false).

### 1.3. Frontend — build de demo (con login bonito)

El SPA de demo apunta al Cloud Run de demo y **muestra un login pulido** (decisión), sin fricción para el reclutador.

- **Auth (decisión: login bonito, no abierto):** la API de demo corre en `Auth:Mode=Dev` (JWT HS256 local,
  ADR-012). El SPA muestra una **pantalla de login pulida** con un botón **"Entrar a la demo" de un clic**
  (y opcionalmente un selector de rol operador/admin, para lucir el RBAC). El clic dispara la adquisición
  del token dev (`/auth/dev-token?role=…`) y entra. Esto **demuestra el JWT+RBAC** sin pedir credenciales.
  - Cambio de frontend: hoy el modo `'dev'` del `AuthService` adquiere el token **silenciosamente** en
    `provideAppInitializer`. Para la demo se quiere **visible**: NO auto-loguear; mostrar el componente
    `Login` (ya existe) con el botón que llama a `AuthService` al hacer clic. Pequeño ajuste de flujo +
    pulido visual de la pantalla.
  - (Alternativa si quieres OIDC real: `Auth:Mode=Oidc` + Identity Platform con un usuario demo
    pre-creado y autologin — más "real" pero pide gestionar una credencial; el Dev-login es $0 y suficiente.)
- **API URL:** un `demoApiConfig` con la URL del Cloud Run de demo.
- **Selección:** añadir una **configuración de build `demo`** en `apps/atalaya-web/project.json` con
  `fileReplacements` que cambie un `deploy-target.ts` (o el `app.config.ts`) por su variante demo (API URL
  de demo + auth modo `'dev'` con login visible). Evita tocar `isDevMode()`. Comando:
  `nx build atalaya-web --configuration=demo`.

### 1.4. Infra — Cloud Run de demo (IaC dedicado, estado propio)

**Directorio nuevo:** `infra/terraform-demo/` (estado separado: `prefix=atalaya/demo/state` en el bucket
`gs://atalaya-tfstate`). Recursos:

- `google_cloud_run_v2_service "demo_api"`:
  - `image` = `us-central1-docker.pkg.dev/fabian-portafolio/atalaya/api:demo` (de la base persistente).
  - `scaling { min_instance_count = 0; max_instance_count = 1 }` (scale-to-zero; sin caché de incidentes a coordinar porque es 1 sola instancia).
  - `template.containers.resources { cpu_idle = true }` (clave para el $0; el generador corre durante el WebSocket).
  - **Sin** `vpc_access` ni `cloud_sql` (no hay Redis ni Postgres).
  - env: `ASPNETCORE_ENVIRONMENT=Production`, `Telemetry__Transport=InMemory`, `Auth__Mode=Dev`,
    `Auth__DevSigningKey=<clave-hs256>` (+ `Auth__Issuer`/`Auth__Audience`), `Demo__Enabled=true`,
    `Demo__Devices=40`, `Cors__Origins__0=https://atalaya-dashboard.web.app`.
  - `ingress = INGRESS_TRAFFIC_ALL` + IAM `allUsers` → `roles/run.invoker` (público).
- (Opcional) `google_firebase_hosting_site "demo"` si se usa un sitio dedicado.

> Alternativa rápida sin IaC (un comando): `gcloud run deploy atalaya-demo-api --image=…/api:demo
> --region=us-central1 --allow-unauthenticated --min-instances=0 --set-env-vars=Telemetry__Transport=InMemory,Auth__Mode=Disabled,Demo__Enabled=true,Cors__Origins__0=https://…`.
> El IaC dedicado se prefiere por coherencia de portafolio (todo es Terraform).

### 1.5. Pasos de ejecución (Nivel 1)

1. **Código:** crear `DemoTelemetryGenerator.cs` + `DemoOptions` + registro en `Program.cs`. Unit test del generador (genera N eventos por tick; un caso que cruza umbral → alerta).
2. **Base persistente (una vez):** recrear Artifact Registry (`terraform apply -target=google_artifact_registry_repository.atalaya` en `infra/terraform/`, o vía `terraform-demo`), construir y publicar `api:demo` (`docker build -f apps/api/Dockerfile -t …/api:demo . && docker push …`).
3. **Cloud Run demo:** `cd infra/terraform-demo && terraform init -backend-config="bucket=atalaya-tfstate" && terraform apply` → output `demo_api_url`.
4. **Frontend:** rellenar `demoApiConfig` con `demo_api_url`, auth modo `'dev'` con login visible (pulir pantalla `Login` + botón "Entrar a la demo"); `nx build atalaya-web --configuration=demo`; `firebase deploy --only hosting:demo` (sitio `atalaya-dashboard`).
5. **Verificar:** abrir el sitio → el mapa se llena de vehículos en movimiento, las alertas aparecen, el histórico carga; cerrar la pestaña y confirmar (consola Cloud Run) que el instance escala a 0.

### 1.6. Esfuerzo Nivel 1
**~½ día.** 1 archivo de código + 1 unit test, ~30 líneas de Terraform, una config de build y un deploy. (Real: hecho en una sesión.)

### 1.7. Operación del Nivel 1 (lo que quedó desplegado, 2026-06-25)

- **URLs:** SPA `https://atalaya-demo.web.app` · API `https://atalaya-demo-api-aqeprs2exa-uc.a.run.app`.
  > El sitio se llama **`atalaya-demo`** (no `atalaya-dashboard`): tras el teardown de G5b, Firebase
  > **reservó** ese ID por un tiempo, así que se creó uno nuevo (`firebase hosting:sites:create atalaya-demo`).
- **Componentes:** Cloud Run `atalaya-demo-api` (estado en `infra/terraform-demo/`, backend GCS prefijo
  `atalaya/demo/state`) · imagen `api:demo` en Artifact Registry · sitio Firebase `atalaya-demo`.
- **Costo:** ~$0 sostenido (Cloud Run min-instances=0 + cpu_idle=true; Hosting gratis; AR ≈ centavos/mes).
  **No requiere teardown** — es el nivel always-on.
- **Re-desplegar tras un cambio de código del backend:**
  ```
  docker build -f apps/api/Dockerfile -t us-central1-docker.pkg.dev/fabian-portafolio/atalaya/api:demo . && docker push …/api:demo
  D:\tools\terraform\terraform.exe -chdir=infra/terraform-demo apply -var="project_id=fabian-portafolio" -var="demo_image=…/api:demo"
  # Cloud Run no redespliega si el tag no cambia; forzar nueva revisión: usar un tag nuevo (api:demo2) o `gcloud run services update atalaya-demo-api --region us-central1`.
  ```
- **Re-desplegar el frontend:** `nx build atalaya-web --configuration=demo && firebase deploy --only hosting --project fabian-portafolio`.
- **Apagar/borrar la demo (si alguna vez):** `D:\tools\terraform\terraform.exe -chdir=infra/terraform-demo destroy -var="project_id=fabian-portafolio" -var="demo_image=…/api:demo"` (+ `firebase hosting:sites:delete atalaya-demo`).

---

## 2. Nivel 2 — Stack efímero completo (solo lo que uses)

### 2.1. Idea

Reusar el Terraform de G5b, pero **partido** para poder encender/apagar solo lo que cobra, sin tocar la
base persistente ni reconstruir imágenes.

**Clasificación de recursos:**

| Persistente (NO se destruye, ~$0) | Efímero (se enciende/apaga, cobra) |
|---|---|
| Artifact Registry + imágenes `api`/`worker` | Cloud SQL (`sql.tf`) |
| Bucket `atalaya-datalake` | Memorystore Redis (`redis.tf`) |
| Dataset/tabla BigQuery | VPC connector (`network.tf`) |
| Topics/subs Pub/Sub + DLQ, IAM | Cloud Run API + Worker (`cloudrun.tf`) |
| Service Accounts, secrets, APIs habilitadas | (la red base VPC/subnet puede quedarse; es gratis) |

### 2.2. Dos opciones de implementación

- **Opción A (rápida, sin refactor): `terraform destroy -target`.** Mantener el `infra/terraform/`
  actual y apagar solo los billables:
  ```
  terraform destroy \
    -target=google_cloud_run_v2_service.api \
    -target=google_cloud_run_v2_service.worker \
    -target=google_sql_database_instance.main \
    -target=google_redis_instance.cache \
    -target=google_vpc_access_connector.connector
  ```
  `up` = `terraform apply` normal. Pro: cero refactor. Contra: `-target` es frágil con dependencias; hay que listar bien.
- **Opción B (limpia): dos raíces de Terraform.** `infra/terraform-base/` (persistente) + `infra/terraform-live/`
  (efímero, lee outputs de base por `terraform_remote_state` o `data` sources). `up`/`down` operan solo
  sobre `terraform-live/`. Pro: separación real, `destroy` total de `live` sin riesgo. Contra: refactor de
  un día (mover recursos entre estados con `terraform state mv` o re-import).

**Recomendado:** empezar con **A** (funciona ya), migrar a **B** cuando haya tiempo.

### 2.3. URL estable para el SPA "live" (evitar rebuild por cada apply)

La URL de Cloud Run puede cambiar si el servicio se recrea. Para no reconstruir el SPA cada vez:

- **Config en runtime:** el SPA "live" lee `/assets/config.json` al arrancar (en vez de compilar la URL).
  El script `demo-up` escribe el `api_url` actual en ese JSON **antes** del `firebase deploy`. Así el build
  del SPA es fijo y solo cambia un JSON.
- Alternativa: **dominio personalizado** mapeado al Cloud Run (URL estable), más setup inicial.

### 2.4. Scripts up/down

**`infra/demo-up.ps1`** (orquesta el encendido):
1. Verifica que existan las imágenes en Artifact Registry (si no, build+push).
2. `terraform apply` (Opción A) o en `terraform-live/` (Opción B). (~15–20 min: Cloud SQL es el cuello.)
3. Lee outputs (`api_url`); escribe `assets/config.json`; `firebase deploy --only hosting:live`.
4. Smoke rápido: `curl $api_url/health/ready` (espera 200) + ingest de prueba.
5. Imprime la URL del SPA live para compartir.

**`infra/demo-down.ps1`:** `terraform destroy` de los billables (Opción A `-target`, u Opción B raíz `live`).
Deja intacta la base persistente.

> Agendar el `up` **~25–30 min antes** de la entrevista (margen por Cloud SQL).

### 2.5. Pasos de ejecución (Nivel 2)
1. Decidir Opción A o B (recomendado A primero).
2. (A) escribir `demo-up.ps1`/`demo-down.ps1` con los `-target`. (B) partir el Terraform en base/live.
3. SPA live → config en runtime (`assets/config.json`) + sitio de hosting `live`.
4. Ensayo completo: `up` → demo en vivo + consola GCP (Pub/Sub, autoescalado) → `down` → verificar que solo queda la base.

### 2.6. Esfuerzo Nivel 2
- Opción A: **~2–3 h** (scripts + runtime config + ensayo).
- Opción B: **+1 día** (refactor de estados).

---

## 3. Orden sugerido y decisiones abiertas

**Orden:** Nivel 1 primero (mayor impacto: link 24/7 para el 95% de reclutadores), luego Nivel 2 Opción A.

**Decisiones (resueltas 2026-06-25):**
1. **Hosting: dos sitios** ✅ — **`atalaya-demo.web.app`** (demo always-on, **YA EN VIVO**) + `atalaya-live.web.app` (stack efímero, pendiente). Evita el "baile" de redespliegues y que el link público quede roto. (Se usó `atalaya-demo` porque `atalaya-dashboard` quedó reservado tras el teardown de G5b.)
2. **Nivel 2: Opción A** (rápida, `destroy -target`) como primer paso; migrar a B si se quiere pulir. (Nivel 2 es opcional; se puede vivir solo del Nivel 1 + docs + video.)
3. **Generador: vistoso** ✅ — movimiento fluido + alertas disparadas a propósito cada N ticks.
4. **Auth: login bonito** ✅ — `Auth:Mode=Dev` + pantalla de login pulida con botón "Entrar a la demo" de un clic (luce JWT+RBAC sin pedir credenciales). No abierto.

**Costo de referencia:** Nivel 1 ≈ $0 (free tier de Cloud Run + Hosting). Nivel 2 ≈ Cloud SQL `db-f1-micro`
+ Redis BASIC 1 GB + connector + 2× Cloud Run ≈ **US$0.50–2/hora encendido** → una entrevista de 1–2 h cuesta
centavos. Base persistente: almacenamiento + AR ≈ centavos/mes. **Mantener Budget+Alert siempre.**

---

## 4. Checklists

**Nivel 1 (always-on $0) — ✅ COMPLETO (2026-06-25):**
- [x] `DemoTelemetryGenerator` (vistoso: movimiento + alertas) + `DemoOptions` + registro en `Program.cs` + unit test.
- [x] Pantalla `Login` pulida + botón "Entrar a la demo" (auth modo `dev` visible, sin auto-login).
- [x] Artifact Registry recreado + imagen `api:demo` publicada.
- [x] `infra/terraform-demo/` (Cloud Run demo, scale-to-zero, público, `Auth:Mode=Dev`) aplicado.
- [x] SPA build `demo` (`demoApiConfig` + auth `dev`) + `firebase deploy` al sitio `atalaya-demo`.
- [x] Verificado: SPA 200, CORS OK, API InMemory con 40 dispositivos, 401 sin token / 200 con token.

**Nivel 2 (efímero):**
- [ ] Clasificación persistente/efímero implementada (Opción A `-target` o B dos raíces).
- [ ] `demo-up.ps1` / `demo-down.ps1`.
- [ ] SPA live con config en runtime (`assets/config.json`) + sitio `live`.
- [ ] Ensayo `up` → demo + consola GCP → `down` → solo queda la base.
