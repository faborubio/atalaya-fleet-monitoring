import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { appRoutes } from './app.routes';
import { API_CONFIG, devApiConfig } from './core/api.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Zoneless (ADR-010): el render lo gobiernan los signals, no zone.js. El firehose de
    // SignalR ya no dispara change detection; solo el set coalescido cada 100 ms.
    provideZonelessChangeDetection(),
    provideRouter(appRoutes),
    provideHttpClient(withFetch()),
    { provide: API_CONFIG, useValue: devApiConfig },
  ],
};
