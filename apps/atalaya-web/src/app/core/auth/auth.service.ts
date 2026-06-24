import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_CONFIG } from '../api.config';

interface DevTokenResponse {
  token: string;
  role: string;
  expiresIn: number;
}

/**
 * Auth de lecturas (AUD-015 D, SAD §6.1). En modo dev el backend expone `/auth/dev-token`, que
 * mintea un JWT con rol; aquí lo adquirimos de forma silenciosa al arrancar (auto-token) y lo
 * servimos al interceptor HTTP y al `accessTokenFactory` de SignalR. Sin pantalla de login: el
 * foco es demostrar la cadena de auth de extremo a extremo, no la UI de credenciales. En prod este
 * servicio se cambiaría por un flujo OIDC real (MSAL/oidc-client) sin tocar a sus consumidores.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private token: string | null = null;
  private expiresAt = 0;

  /** Rol del usuario actual (`null` si la auth está desactivada). Útil para gating de UI futuro. */
  readonly role = signal<string | null>(null);

  /** Lo invoca el APP_INITIALIZER: adquiere el token antes de que arranque la app. */
  async initialize(): Promise<void> {
    await this.refresh();
  }

  /** Token vigente para el interceptor HTTP (`null` = sin auth, no adjunta cabecera). */
  getToken(): string | null {
    return this.token;
  }

  /** Para SignalR: refresca si está por expirar y devuelve el token (cadena vacía si no hay auth). */
  async ensureToken(): Promise<string> {
    if (!this.token || Date.now() > this.expiresAt - 30_000) await this.refresh();
    return this.token ?? '';
  }

  private async refresh(): Promise<void> {
    try {
      const res = await firstValueFrom(
        this.http.get<DevTokenResponse>(`${this.config.baseUrl}/auth/dev-token`, {
          params: { role: 'operador' },
        })
      );
      this.token = res.token;
      this.role.set(res.role);
      this.expiresAt = Date.now() + res.expiresIn * 1000;
    } catch {
      // Auth:Disabled → `/auth/dev-token` no existe (404): seguimos sin token (lecturas abiertas).
      this.token = null;
      this.role.set(null);
    }
  }
}
