import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FleetStore } from './core/telemetry/fleet-store';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  protected readonly fleet = inject(FleetStore);

  protected readonly title = 'Atalaya';
  protected readonly nav = [
    { path: 'dashboard', label: 'Mapa en vivo' },
    { path: 'devices', label: 'Dispositivos' },
    { path: 'alerts', label: 'Alertas' },
    { path: 'history', label: 'Históricos' },
  ] as const;

  ngOnInit(): void {
    // Arranca el camino caliente a nivel de app: snapshot + stream SignalR.
    void this.fleet.start();
  }
}
