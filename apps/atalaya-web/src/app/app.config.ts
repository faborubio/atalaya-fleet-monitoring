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
import { API_CONFIG, devApiConfig } from './core/api.config';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Zoneless (ADR-010): el render lo gobiernan los signals, no zone.js. El firehose de
    // SignalR ya no dispara change detection; solo el set coalescido cada 100 ms.
    provideZonelessChangeDetection(),
    provideRouter(appRoutes),
    // Auth de lecturas (AUD-015 D): el interceptor adjunta el JWT a /api/* y el initializer
    // adquiere el token (auto-token dev) antes de que arranquen los stores.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideAppInitializer(() => inject(AuthService).initialize()),
    { provide: API_CONFIG, useValue: devApiConfig },
  ],
};
