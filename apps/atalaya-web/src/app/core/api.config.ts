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
