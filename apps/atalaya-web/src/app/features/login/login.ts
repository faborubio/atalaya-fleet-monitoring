import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Pantalla de login (G3): email/password contra Identity Platform (Firebase Auth). Solo se muestra
 * en modo `firebase`; en `dev`/`disabled` el dashboard entra directo. Tras autenticar, el shell
 * (`App`) detecta `authenticated()` y arranca el camino caliente.
 */
@Component({
  selector: 'app-login',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="login">
      <form class="login__card" (ngSubmit)="submit()">
        <h1 class="login__brand">Atalaya</h1>
        <p class="login__sub">Inicia sesión para ver la flota en vivo</p>

        <label class="login__field">
          <span>Email</span>
          <input type="email" name="email" [(ngModel)]="email" autocomplete="username" required />
        </label>
        <label class="login__field">
          <span>Contraseña</span>
          <input type="password" name="password" [(ngModel)]="password"
                 autocomplete="current-password" required />
        </label>

        @if (auth.error()) {
          <p class="login__error">{{ auth.error() }}</p>
        }

        <button class="login__btn" type="submit" [disabled]="busy()">
          {{ busy() ? 'Entrando…' : 'Entrar' }}
        </button>
      </form>
    </div>
  `,
  styles: [
    `
      .login { min-height: 100vh; display: grid; place-items: center; background: #0b1020; }
      .login__card {
        display: flex; flex-direction: column; gap: 0.75rem; width: min(360px, 90vw);
        padding: 2rem; background: #141a2e; border: 1px solid #243049; border-radius: 12px;
        color: #e8eefc;
      }
      .login__brand { margin: 0; font-size: 1.6rem; letter-spacing: 0.04em; }
      .login__sub { margin: 0 0 0.5rem; color: #8aa0c8; font-size: 0.9rem; }
      .login__field { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.85rem; }
      .login__field input {
        padding: 0.55rem 0.7rem; border-radius: 8px; border: 1px solid #2c3a59;
        background: #0d1424; color: #e8eefc;
      }
      .login__error { margin: 0; color: #ff8080; font-size: 0.85rem; }
      .login__btn {
        margin-top: 0.5rem; padding: 0.6rem; border: 0; border-radius: 8px; cursor: pointer;
        background: #3b82f6; color: white; font-weight: 600;
      }
      .login__btn:disabled { opacity: 0.6; cursor: default; }
    `,
  ],
})
export class Login {
  protected readonly auth = inject(AuthService);
  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);

  async submit(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      await this.auth.signIn(this.email, this.password);
    } catch {
      // el mensaje ya está en auth.error()
    } finally {
      this.busy.set(false);
    }
  }
}
