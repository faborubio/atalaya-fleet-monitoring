# TROUBLESHOOTING.md — Errores y soluciones

Registro de problemas encontrados durante el desarrollo de Atalaya **y cómo se
resolvieron**. El objetivo es no tropezar dos veces con la misma piedra.

## Cómo usar este archivo

- Una entrada por problema, con ID `TS-NNN`, **la más reciente arriba** (salvo los
  bloqueos fundacionales, que se mantienen visibles hasta resolverse).
- Antes de pelear con un error nuevo, busca aquí (`Ctrl+F`) por síntoma.
- Cuando resuelvas algo no trivial, **regístralo** aunque parezca obvio en el momento.

### Plantilla

```markdown
## TS-NNN — <título corto del síntoma>

**Fecha:** YYYY-MM-DD · **Área:** frontend/backend/infra/CI · **Estado:** Abierto/Resuelto

**Síntoma**
<qué se ve: mensaje de error, comportamiento>

**Causa raíz**
<por qué pasa de verdad>

**Solución**
<pasos / comando que lo arregla>

**Prevención**
<cómo evitar que vuelva a pasar>
```

---

## TS-008 — Apps .NET no conectan a Redis/Postgres por `localhost` tras reiniciar Docker

**Fecha:** 2026-06-24 · **Área:** infra · **Estado:** ✅ Resuelto (2026-06-24)

**Síntoma**
```
Unhandled exception. StackExchange.Redis.RedisConnectionException: It was not possible to
connect to the redis server(s). Error connecting right now...
   at Atalaya.Realtime.ServiceCollectionExtensions...AddAtalayaRedis...
```
La API/worker fallan al arrancar (exit 82) **aunque** `docker ps` muestra los contenedores `healthy`
y `Test-NetConnection localhost 6379` da `True`. `docker exec ... redis-cli ping` responde `PONG`.

**Causa raíz**
Dos cosas combinadas tras **reiniciar Docker Desktop**: (1) el reenvío de puertos del host tarda unos
segundos en quedar operativo aun después de `healthy` (los apps arrancan demasiado pronto y conectan
fail-fast); (2) `localhost` resuelve primero a **IPv6 (`::1`)** y el proxy IPv6 de Docker (`[::]:6379`)
queda flaky, mientras el IPv4 (`0.0.0.0:6379`) sí funciona.

**Solución**
Esperar a que la infra esté `healthy` **y** forzar **IPv4** en las connection strings (env):
```
$env:ConnectionStrings__Redis="127.0.0.1:6379"
$env:ConnectionStrings__Postgres="Host=127.0.0.1;Port=5432;Database=atalaya;Username=atalaya;Password=atalaya"
```

**Prevención**
Tras un restart de Docker, validar el puerto con un cliente real (no solo `Test-NetConnection`, que solo
hace el SYN) antes de arrancar; preferir `127.0.0.1` a `localhost` en dev. Matar procesos `dotnet`
zombie (`Stop-Process -Name dotnet,Atalaya.Api,Atalaya.Worker`) entre reintentos: instancias a medio
arrancar compiten por suscripciones Pub/Sub y confunden el diagnóstico (p.ej. el replay de la DLQ
reportando conteo 0 mientras otra instancia reprocesa, AUD-027).

---

## TS-007 — `cdklocal` rompe con la CLI nueva de aws-cdk (`lib/cdk-toolkit` not exported)

**Fecha:** 2026-06-22 · **Área:** infra · **Estado:** ✅ Resuelto (2026-06-22)

**Síntoma**
```
Error [ERR_PACKAGE_PATH_NOT_EXPORTED]: Package subpath './lib/cdk-toolkit' is not defined
by "exports" in .../node_modules/aws-cdk/package.json
```
Al correr `cdklocal bootstrap`/`deploy` con `aws-cdk` 2.1xxx instalado.

**Causa raíz**
La CLI de `aws-cdk` se re-empaquetó en la línea **2.1xxx** y dejó de exportar
`lib/cdk-toolkit`. `aws-cdk-local` **2.x** (que parchea ese módulo interno) ya no es compatible.

**Solución**
Usar **`aws-cdk-local` 3.x** con la CLI nueva. En `infra/cdk/package.json`:
`"aws-cdk": "^2.1100.0"` + `"aws-cdk-local": "^3.0.4"`. Reinstalar y reintentar; el deploy a
LocalStack vuelve a funcionar (stack `AtalayaStack` desplegado y verificado).

**Prevención**
Mantener emparejados el major de `aws-cdk-local` (3.x) con la CLI 2.1xxx. `cdk synth` no depende
de cdklocal, así que sirve como verificación offline independiente del problema.

---

## TS-001 — No hay .NET SDK (solo runtime)

**Fecha:** 2026-06-21 · **Área:** backend · **Estado:** ✅ Resuelto (2026-06-21)

**Resolución:** instalado **.NET SDK 8.0.422** con
`winget install Microsoft.DotNet.SDK.8 --silent`. Verificado con `dotnet --list-sdks`.
Tras instalar puede hacer falta reabrir la terminal para refrescar el PATH.

**Síntoma**
```
$ dotnet --version
No .NET SDKs were found.
$ dotnet --list-sdks      # vacío
$ dotnet --list-runtimes  # Microsoft.NETCore.App 6.0.36 (solo runtime)
```

**Causa raíz**
La máquina tiene el **runtime** de .NET 6 pero **no el SDK**. Sin SDK no se puede
`dotnet new`, `dotnet build` ni `dotnet run` de proyectos nuevos.

**Solución**
Instalar el **.NET SDK 8 (LTS)** desde https://aka.ms/dotnet-download (o `winget install
Microsoft.DotNet.SDK.8`). Verificar con `dotnet --list-sdks` (debe listar 8.x). Reiniciar
la terminal/IDE para refrescar el PATH.

**Prevención**
Documentado como prerequisito en [DEPLOY.md §0](./DEPLOY.md#0-prerrequisitos). El backend
.NET no se scaffoldea hasta tener SDK.

---

## TS-002 — Docker no disponible

**Fecha:** 2026-06-21 · **Área:** infra · **Estado:** ✅ Resuelto (2026-06-21)

**Resolución:** instalado **Docker Desktop** (`winget install Docker.DockerDesktop --silent`).
La máquina ya tenía **WSL2** (Ubuntu-24.04, v2), que es el backend requerido. Verificado:
Docker 29.5.3, Compose v5.1.4, `docker run hello-world` OK. Tras instalar hay que lanzar
Docker Desktop una vez (aceptar el acuerdo) para que el daemon arranque.

**Síntoma**
```
$ docker --version
docker: command not found      # (y no aparece en el PATH de Windows)
```

**Causa raíz**
Docker Desktop no está instalado. El pipeline event-driven se reproduce en local con
**LocalStack** (SNS/SQS/S3/Lambda) + Redis + PostgreSQL, todo sobre Docker (ADR-009).
Sin Docker no hay infra local ni Testcontainers para los tests de integración.

**Solución**
Instalar **Docker Desktop** (https://www.docker.com/products/docker-desktop/ o `winget
install Docker.DockerDesktop`), habilitar el backend WSL2, iniciarlo y verificar con
`docker run hello-world`.

**Prevención**
Documentado como prerequisito en [DEPLOY.md §0](./DEPLOY.md#0-prerrequisitos). La infra y
los tests de integración quedan diferidos hasta tener Docker operativo.

---

## TS-003 — Nx 23 incompatible con Angular (TS solution setup)

**Fecha:** 2026-06-21 · **Área:** frontend/tooling · **Estado:** ✅ Resuelto

**Síntoma**
Al añadir `@nx/angular` sobre un workspace creado con `create-nx-workspace@latest` (Nx 23):
```
NX  The "@nx/angular" plugin doesn't support the existing TypeScript setup
    The Angular framework doesn't support a TypeScript setup with project references.
```
Además, el preset `angular-monorepo` en Nx 23 descarga una **demo fullstack** (apps
`shop`/`api`) en vez de un esqueleto limpio, y mete config de varios agentes
(`.gemini`, `.codex`, `opencode.json`, etc.).

**Causa raíz**
Nx 23 usa por defecto el *TS solution setup*: `tsconfig.base.json` con `composite: true`
y `project references`. El compilador de Angular (ngtsc) **no soporta project references**.
El flag `--workspaces=false` no ayuda porque los templates lo ignoran.

**Solución**
Generar el workspace con **Nx 21** (layout integrado clásico, `paths` en
`tsconfig.base.json`, sin project references), que produce una sola app Angular limpia:
```bash
npx create-nx-workspace@21 atalaya --preset=angular-monorepo --appName=atalaya-web \
  --style=scss --routing=true --standaloneApi=true --bundler=esbuild --ssr=false \
  --unitTestRunner=jest --e2eTestRunner=none --nxCloud=skip --packageManager=npm \
  --interactive=false --skipGit
```

**Prevención**
Quedarse en Nx 21 mientras dure el desarrollo de Fase 0–3. Para subir a Nx 23 más
adelante, usar la migración asistida `npx nx migrate latest` (no recrear el workspace).

---

## TS-004 — `dotnet` no resuelve paquetes NuGet (sin fuentes)

**Fecha:** 2026-06-21 · **Área:** backend/tooling · **Estado:** ✅ Resuelto

**Síntoma**
```
error NU1100: No se puede resolver 'Microsoft.Extensions.Hosting (>= 8.0.1)' para 'net8.0'.
$ dotnet nuget list source
No se encontró ningún origen.
```

**Causa raíz**
La máquina no tenía **ninguna fuente NuGet** configurada (ni siquiera nuget.org), así que
el restore de cualquier paquete fallaba.

**Solución**
Se versionó un [`nuget.config`](./nuget.config) en la raíz del repo declarando nuget.org.
Así el restore es reproducible sin depender de la config global:
```xml
<packageSources>
  <clear />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
</packageSources>
```

**Prevención**
El `nuget.config` del repo cubre cualquier clon. Para añadirla globalmente:
`dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`.

---

## TS-005 — `Microsoft.AspNetCore.Mvc.Testing` incompatible con net8.0

**Fecha:** 2026-06-21 · **Área:** backend/test · **Estado:** ✅ Resuelto

**Síntoma**
```
error NU1202: El paquete Microsoft.AspNetCore.Mvc.Testing 10.0.9 no es compatible con
net8.0. ... admite: net10.0
```

**Causa raíz**
`dotnet add package` instala la **última** versión por defecto (10.x → net10), pero los
proyectos son net8.0.

**Solución**
Fijar la versión 8.x: `dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.0.11`.

**Prevención**
Al añadir paquetes de ASP.NET Core a proyectos net8, fijar siempre `--version 8.0.x`
(van alineados con el runtime).

---

## TS-006 — Disco C: lleno (0 bytes) → Docker en modo solo-lectura

**Fecha:** 2026-06-21 · **Área:** infra/sistema · **Estado:** ✅ Resuelto

**Síntoma**
Al hacer `docker pull` de imágenes grandes (LocalStack):
```
failed to extract layer ...: read-only file system
write /var/lib/desktop-containerd/.../meta.db: read-only file system
```

**Causa raíz**
El disco **C: estaba a 0 bytes libres**. Docker (containerd, en la VM WSL2) se quedó sin
espacio al extraer y entró en modo solo-lectura. La VM de datos de Docker vivía en C:.

**Solución (dos partes)**
1. **Liberar C:** Papelera + `%TEMP%` + `npm cache clean --force` (todo reversible).
   ⚠️ No borrar `%TEMP%\claude` (lo usa el harness para la salida de comandos).
2. **Mover los datos de Docker a D:** (51 GB libres). Con Docker Desktop **parado** y
   `wsl --shutdown`:
   - `wsl --manage docker-desktop --move "D:\DockerData"` (mueve la distro de sistema).
   - El disco de datos (imágenes) es un vhdx aparte en
     `%LOCALAPPDATA%\Docker\wsl\disk\docker_data.vhdx`. Se movió a `D:\DockerData\disk\`
     y se dejó un **junction** en la ruta original:
     `New-Item -ItemType Junction -Path <C:\...\wsl\disk> -Target D:\DockerData\disk`.
   - Editar `DataFolder` en `%APPDATA%\Docker\settings-store.json` **no** mueve nada
     (solo aplica desde la GUI); por eso el junction.

**Prevención**
Mantener el almacén de Docker en D:. Vigilar espacio en C: antes de pulls grandes.

---

## TS-007 — LocalStack no arranca: `exec format error` y luego license token

**Fecha:** 2026-06-21 · **Área:** infra · **Estado:** ✅ Resuelto

**Síntoma**
- `localstack/localstack:3.8`: contenedor sale con `exec /usr/local/bin/docker-entrypoint.sh: exec format error`.
- `localstack/localstack:latest`: sale con código 55, *"License activation failed! No credentials… set LOCALSTACK_AUTH_TOKEN"*.

**Causa raíz**
- El `exec format error` no era arquitectura (todo amd64): la **capa cacheada de 3.8
  quedó corrupta** (entrypoint con 0 líneas/bytes basura), secuela del disco lleno
  ([TS-006](#ts-006--disco-c-lleno-0-bytes--docker-en-modo-solo-lectura)). `docker pull`
  no la re-bajaba ("up to date") porque el digest seguía en caché.
- `latest` apunta a builds **2026.x de pago** que exigen token.

**Solución**
Usar una versión **3.x community** con digest distinto al corrupto: `localstack/localstack:3.7`
(entrypoint íntegro, gratis). Verificar integridad antes de usar:
```bash
docker run --rm --entrypoint sh localstack/localstack:3.7 -c "wc -l /usr/local/bin/docker-entrypoint.sh"
# debe dar ~39 líneas, no 0
```

**Prevención**
Fijar LocalStack a una versión `3.x` concreta (nunca `latest`). Si una imagen da
`exec format error` con arquitectura correcta, sospechar capa corrupta: re-pull forzado o
cambiar de versión para obtener otro digest.

---

> Mantén este archivo cerca: cada error resuelto que registres aquí es tiempo que no
> vuelves a perder.
