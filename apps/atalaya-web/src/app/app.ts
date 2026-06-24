import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FleetStore } from './core/telemetry/fleet-store';
import { AlertStore } from './core/telemetry/alert-store';
import { AuthService } from './core/auth/auth.service';
import { Login } from './features/login/login';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Login],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly fleet = inject(FleetStore);
  protected readonly alerts = inject(AlertStore);
  protected readonly auth = inject(AuthService);

  protected readonly title = 'Atalaya';
  protected readonly nav = [
    { path: 'dashboard', label: 'Mapa en vivo' },
    { path: 'devices', label: 'Dispositivos' },
    { path: 'alerts', label: 'Alertas' },
    { path: 'history', label: 'Históricos' },
  ] as const;

  /** En modo firebase la app exige login; en dev/disabled entra directo. */
  private readonly requiresLogin = this.auth.mode === 'firebase';
  protected readonly canSignOut = this.requiresLogin;
  protected readonly showApp = computed(() => !this.requiresLogin || this.auth.authenticated());

  private active = false;

  constructor() {
    // Ata el camino caliente al estado de sesión (G3): arranca al autenticar (o si no se exige
    // login), y para al cerrar sesión — así no queda un WebSocket autenticado abierto tras el
    // sign-out, y un re-login reconecta con token fresco.
    effect(() => {
      const show = this.showApp();
      if (show && !this.active) {
        this.active = true;
        void this.fleet.start();
        this.alerts.start();
      } else if (!show && this.active) {
        this.active = false;
        void this.fleet.stop();
        this.alerts.stop();
      }
    });
  }

  async signOut(): Promise<void> {
    await this.auth.signOut();
  }
}
