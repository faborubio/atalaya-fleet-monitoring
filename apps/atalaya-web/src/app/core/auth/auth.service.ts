import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { Auth, User } from 'firebase/auth';
import { API_CONFIG } from '../api.config';
import { AUTH_CONFIG, type AuthConfig } from './auth.config';

// Firebase se importa de forma dinámica solo en modo `firebase`: lo mantiene fuera del bundle
// inicial (dev/disabled no lo cargan) y fuera del grafo de Jest. Los tipos van por `import type`
// (se borran en compilación, no disparan carga en runtime).

interface DevTokenResponse {
  token: string;
  role: string;
  expiresIn: number;
}

/**
 * Auth del dashboard (AUD-019 + G3/AUD-023). Dos estrategias según `AUTH_CONFIG.mode`:
 * - `dev`: adquiere un JWT HS256 de `/auth/dev-token` al arrancar (silencioso, sin login).
 * - `firebase`: login real con Identity Platform; el ID token (y el rol por custom claim) salen
 *   de Firebase Auth, que refresca el token solo. `disabled`: sin token (lecturas abiertas).
 * Sirve el token al `authInterceptor` (REST) y al `accessTokenFactory` del hub, igual en ambos modos.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly apiConfig = inject(API_CONFIG);
  private readonly config = inject(AUTH_CONFIG);

  private token: string | null = null;
  private expiresAt = 0; // epoch ms de expiración del token vigente (dev y firebase)
  private firebaseAuth?: Auth;
  private user: User | null = null;
  private devRole = 'operador'; // rol elegido para /auth/dev-token (modos dev y demo)

  /** Rol del usuario (de `/auth/dev-token` o del custom claim de Firebase). */
  readonly role = signal<string | null>(null);
  /** Hay sesión válida. En modo `disabled` es siempre true (no se exige login). */
  readonly authenticated = signal(false);
  /** Mensaje de error del último intento de login (para la UI). */
  readonly error = signal<string | null>(null);

  get mode(): AuthConfig['mode'] {
    return this.config.mode;
  }

  /** Lo invoca el APP_INITIALIZER: deja el token listo (dev) o restaura la sesión (firebase). */
  async initialize(): Promise<void> {
    if (this.config.mode === 'firebase') {
      await this.initFirebase();
    } else if (this.config.mode === 'dev') {
      await this.refreshDevToken();
    } else if (this.config.mode === 'demo') {
      // No auto-loguea: el shell muestra el login y el usuario entra con un clic (loginDemo).
      this.authenticated.set(false);
    } else {
      this.authenticated.set(true); // disabled: sin auth, entra directo
    }
  }

  /**
   * Login de un clic para la demo de portafolio (modo `demo`, ADR-014): adquiere un token dev con el
   * rol elegido y entra. Lanza con un mensaje legible si el backend no responde.
   */
  async loginDemo(role: 'operador' | 'admin'): Promise<void> {
    this.error.set(null);
    this.devRole = role;
    await this.refreshDevToken();
    if (!this.authenticated()) {
      this.error.set('No se pudo conectar con la API de demo. Intenta de nuevo.');
      throw new Error('demo-login-failed');
    }
  }

  /** Token vigente para el interceptor HTTP (sincrónico; `null` = sin token). */
  getToken(): string | null {
    return this.token;
  }

  /** Epoch ms en que expira el token vigente (0 = sin token / auth desactivada). Lo usa el hub para
   *  reconectar proactivamente antes de expirar (refresh-token en conexiones largas, AUD-030). */
  getTokenExpiry(): number {
    return this.expiresAt;
  }

  /** Para SignalR y refrescos: devuelve el token vigente (refresca si hace falta). */
  async ensureToken(): Promise<string> {
    if (this.config.mode === 'firebase') {
      if (this.user) this.token = await this.user.getIdToken();
      return this.token ?? '';
    }
    if (this.config.mode === 'dev' || this.config.mode === 'demo') {
      if (!this.token || Date.now() > this.expiresAt - 30_000) await this.refreshDevToken();
    }
    return this.token ?? '';
  }

  /** Login con email/password (modo firebase). Lanza con un mensaje legible si falla. */
  async signIn(email: string, password: string): Promise<void> {
    if (!this.firebaseAuth) throw new Error('Firebase no está inicializado.');
    this.error.set(null);
    try {
      const { signInWithEmailAndPassword } = await import('firebase/auth');
      await signInWithEmailAndPassword(this.firebaseAuth, email, password);
      // onIdTokenChanged actualiza token/role/authenticated.
    } catch {
      this.error.set('Credenciales inválidas.');
      throw new Error('signin-failed');
    }
  }

  async signOut(): Promise<void> {
    if (this.config.mode === 'demo' || this.config.mode === 'dev') {
      // Demo/dev: limpia el token local y vuelve al login (no hay sesión remota que cerrar).
      this.token = null;
      this.expiresAt = 0;
      this.role.set(null);
      this.authenticated.set(false);
      return;
    }
    if (!this.firebaseAuth) return;
    const { signOut } = await import('firebase/auth');
    await signOut(this.firebaseAuth);
  }

  private async initFirebase(): Promise<void> {
    const firebaseConfig = this.config.firebase;
    if (!firebaseConfig) throw new Error('Falta la config de Firebase en modo firebase.');

    const { initializeApp } = await import('firebase/app');
    const { getAuth, onIdTokenChanged } = await import('firebase/auth');
    const auth = getAuth(initializeApp(firebaseConfig));
    this.firebaseAuth = auth;

    // Resuelve cuando Firebase entrega el primer estado (sesión restaurada o no).
    await new Promise<void>((resolve) => {
      let first = true;
      onIdTokenChanged(auth, async (user) => {
        this.user = user;
        if (user) {
          this.token = await user.getIdToken();
          const result = await user.getIdTokenResult();
          this.role.set((result.claims['role'] as string | undefined) ?? null);
          this.expiresAt = Date.parse(result.expirationTime); // Firebase: ~1 h, refrescado solo
          this.authenticated.set(true);
        } else {
          this.token = null;
          this.expiresAt = 0;
          this.role.set(null);
          this.authenticated.set(false);
        }
        if (first) {
          first = false;
          resolve();
        }
      });
    });
  }

  private async refreshDevToken(): Promise<void> {
    try {
      const res = await firstValueFrom(
        this.http.get<DevTokenResponse>(`${this.apiConfig.baseUrl}/auth/dev-token`, {
          params: { role: this.devRole },
        })
      );
      this.token = res.token;
      this.role.set(res.role);
      this.expiresAt = Date.now() + res.expiresIn * 1000;
      this.authenticated.set(true);
    } catch {
      // Auth:Disabled → /auth/dev-token no existe (404): seguimos sin token.
      this.token = null;
      this.role.set(null);
      this.authenticated.set(false);
    }
  }
}
