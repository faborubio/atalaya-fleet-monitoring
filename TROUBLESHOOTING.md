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

## TS-001 — No hay .NET SDK (solo runtime)

**Fecha:** 2026-06-21 · **Área:** backend · **Estado:** 🔴 Abierto (bloqueante)

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

**Fecha:** 2026-06-21 · **Área:** infra · **Estado:** 🔴 Abierto (bloqueante)

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

> Mantén este archivo cerca: cada error resuelto que registres aquí es tiempo que no
> vuelves a perder.
