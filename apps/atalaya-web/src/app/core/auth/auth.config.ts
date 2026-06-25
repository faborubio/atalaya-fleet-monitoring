import { InjectionToken } from '@angular/core';

/** Config web de Firebase / Identity Platform (Project settings → Your apps → Web). */
export interface FirebaseWebConfig {
  apiKey: string;
  authDomain: string;
  projectId: string;
}

/**
 * Modo de auth del dashboard (G3, ADR-013):
 * - `dev`: token automático silencioso contra `/auth/dev-token` (modo Auth:Dev del backend).
 * - `demo`: como `dev` (token de `/auth/dev-token`) pero con **pantalla de login visible** y botón de
 *   un clic + selector de rol — para la demo de portafolio (ADR-014): luce el JWT+RBAC sin credenciales.
 * - `firebase`: login real con Identity Platform / Firebase Auth (modo Auth:Oidc).
 * - `disabled`: sin auth (backend Auth:Disabled); el dashboard entra directo sin token.
 */
export interface AuthConfig {
  mode: 'dev' | 'demo' | 'firebase' | 'disabled';
  firebase?: FirebaseWebConfig;
}

export const AUTH_CONFIG = new InjectionToken<AuthConfig>('AUTH_CONFIG');

/** Valor de desarrollo: token dev silencioso (sin pantalla de login). */
export const devAuthConfig: AuthConfig = { mode: 'dev' };

/** Demo de portafolio (ADR-014): login visible de un clic contra el backend en modo Auth:Dev. */
export const demoAuthConfig: AuthConfig = { mode: 'demo' };
