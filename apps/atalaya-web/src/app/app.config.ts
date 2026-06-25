import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { appRoutes } from './app.routes';
import { API_CONFIG } from './core/api.config';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { AUTH_CONFIG } from './core/auth/auth.config';
// La selección dev/prod/demo vive en deploy-target.ts (el build `demo` lo intercambia por su variante).
import { apiConfig, authConfig } from './core/deploy-target';

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
    { provide: API_CONFIG, useValue: apiConfig },
    { provide: AUTH_CONFIG, useValue: authConfig },
  ],
};
