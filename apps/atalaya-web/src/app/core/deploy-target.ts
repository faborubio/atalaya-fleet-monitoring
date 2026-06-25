import { isDevMode } from '@angular/core';
import { devApiConfig, prodApiConfig, type ApiConfig } from './api.config';
import { devAuthConfig, type AuthConfig } from './auth/auth.config';

/**
 * Selección de despliegue por defecto (dev local / producción). El build de **demo de portafolio**
 * (ADR-014) reemplaza este archivo por `deploy-target.demo.ts` vía `fileReplacements`
 * (ver `project.json` → `configurations.demo`), apuntando a la API InMemory de demo + login de un clic.
 */

// Identity Platform real (G3) — proyecto fabian-portafolio. El apiKey web es un identificador
// público del proyecto (no un secreto), por eso puede vivir en el cliente/repo.
const firebaseAuthConfig: AuthConfig = {
  mode: 'firebase',
  firebase: {
    apiKey: 'AIzaSyAiNY460WZX7er8mLy5UCws_06I2ka6qWM',
    authDomain: 'fabian-portafolio.firebaseapp.com',
    projectId: 'fabian-portafolio',
  },
};

// Dev (nx serve) → API local :3000 + token dev silencioso; build de producción (G5b) → API en Cloud
// Run + login real contra Identity Platform.
export const apiConfig: ApiConfig = isDevMode() ? devApiConfig : prodApiConfig;
export const authConfig: AuthConfig = isDevMode() ? devAuthConfig : firebaseAuthConfig;
