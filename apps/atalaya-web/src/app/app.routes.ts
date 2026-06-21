import { Route } from '@angular/router';

/**
 * Rutas de Atalaya — lazy por feature (SAD §4.4).
 * Cada feature carga su componente standalone bajo demanda para mantener el
 * bundle inicial pequeño (NFR: < 250 KB gzip).
 */
export const appRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    title: 'Atalaya — Mapa en vivo',
    loadComponent: () =>
      import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'devices',
    title: 'Atalaya — Dispositivos',
    loadComponent: () =>
      import('./features/devices/devices').then((m) => m.Devices),
  },
  {
    path: 'alerts',
    title: 'Atalaya — Alertas',
    loadComponent: () => import('./features/alerts/alerts').then((m) => m.Alerts),
  },
  {
    path: 'history',
    title: 'Atalaya — Históricos',
    loadComponent: () =>
      import('./features/history/history').then((m) => m.History),
  },
  { path: '**', redirectTo: 'dashboard' },
];
