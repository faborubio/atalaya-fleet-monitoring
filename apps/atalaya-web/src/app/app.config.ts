import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { appRoutes } from './app.routes';
import { API_CONFIG, devApiConfig, prodApiConfig } from './core/api.config';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { AUTH_CONFIG, devAuthConfig, type AuthConfig } from './core/auth/auth.config';

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

// Modo activo del dashboard: `false` = token dev silencioso (offline, desarrollo local);
// `true` = login real contra Identity Platform (demo end-to-end). Un solo flip.
const useFirebaseAuth = true;
const authConfig: AuthConfig = useFirebaseAuth ? firebaseAuthConfig : devAuthConfig;

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Zoneless (ADR-010): el render lo gobiernan los signals, no zone.js. El firehose de
    // SignalR ya no dispara change detection; solo el set coalescido cada 100 ms.
    provideZonelessChangeDetection(),
    provideRouter(appRoutes),
    // Auth de lecturas (AUD-019 + G3): el interceptor adjunta el JWT a /api/* y el initializer
    // deja el token listo (dev) o restaura la sesión Firebase (firebase) antes de arrancar los stores.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideAppInitializer(() => inject(AuthService).initialize()),
    // Dev (nx serve) → API local :3000; build de producción (G5b) → API en Cloud Run (prodApiConfig).
    { provide: API_CONFIG, useValue: isDevMode() ? devApiConfig : prodApiConfig },
    { provide: AUTH_CONFIG, useValue: authConfig },
  ],
};
