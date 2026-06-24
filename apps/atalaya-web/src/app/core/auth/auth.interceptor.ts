import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Adjunta el JWT a las lecturas REST (`/api/*`). El hub SignalR no pasa por aquí: usa su propio
 * `accessTokenFactory`. Usa <b>`ensureToken()`</b> (no el token cacheado): así refresca el token si
 * está por expirar antes de cada lectura — evita 401 por token viejo en sesiones largas (AUD-030).
 * Sin token vigente (Auth:Disabled) la petición sale tal cual.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.includes('/api/')) return next(req);

  const auth = inject(AuthService);
  return from(auth.ensureToken()).pipe(
    switchMap((token) =>
      next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req)
    )
  );
};
