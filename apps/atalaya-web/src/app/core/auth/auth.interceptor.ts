import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Adjunta el JWT a las lecturas REST (`/api/*`). El hub SignalR no pasa por aquí: usa su propio
 * `accessTokenFactory` (el WebSocket no manda cabecera Authorization). Sin token vigente
 * (Auth:Disabled) la petición sale tal cual.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.includes('/api/')) return next(req);

  const token = inject(AuthService).getToken();
  if (!token) return next(req);

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
