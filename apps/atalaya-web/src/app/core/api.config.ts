import { InjectionToken } from '@angular/core';

/** Configuración de endpoints del backend. */
export interface ApiConfig {
  /** Base de la API REST (ingesta/snapshot). */
  baseUrl: string;
  /** URL del hub SignalR del camino caliente. */
  hubUrl: string;
}

export const API_CONFIG = new InjectionToken<ApiConfig>('API_CONFIG');

/** Valores de desarrollo: la API .NET corre en :3000 (ver apps/api launchSettings). */
export const devApiConfig: ApiConfig = {
  baseUrl: 'http://localhost:3000',
  hubUrl: 'http://localhost:3000/hubs/telemetry',
};

/**
 * Producción (G5b): la API corre en **Cloud Run**. Su URL solo se conoce tras el `terraform apply`
 * (output `api_url`). **Rellena `PROD_API_BASE_URL` con esa URL antes del `nx build` de producción +
 * `firebase deploy`** (y recuerda añadir el dominio de Hosting a `cors_origins` en Terraform).
 * La selección dev/prod la hace `app.config.ts` por `isDevMode()`.
 */
const PROD_API_BASE_URL = 'https://atalaya-api-aqeprs2exa-uc.a.run.app';
export const prodApiConfig: ApiConfig = {
  baseUrl: PROD_API_BASE_URL,
  hubUrl: `${PROD_API_BASE_URL}/hubs/telemetry`,
};

/**
 * Demo de portafolio always-on (ADR-014, Nivel 1, ver DEMO.md): un único Cloud Run InMemory con el
 * generador de datos. **Rellena `DEMO_API_BASE_URL` con el output `demo_api_url` del `terraform apply`
 * de `infra/terraform-demo/` antes del `nx build atalaya-web --configuration=demo` + `firebase deploy`.**
 * La selección la hace el build `demo` (fileReplacements de `deploy-target.ts`).
 */
const DEMO_API_BASE_URL = 'https://atalaya-demo-api-aqeprs2exa-uc.a.run.app';
export const demoApiConfig: ApiConfig = {
  baseUrl: DEMO_API_BASE_URL,
  hubUrl: `${DEMO_API_BASE_URL}/hubs/telemetry`,
};
