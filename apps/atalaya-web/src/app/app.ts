import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly title = 'Atalaya';
  protected readonly nav = [
    { path: 'dashboard', label: 'Mapa en vivo' },
    { path: 'devices', label: 'Dispositivos' },
    { path: 'alerts', label: 'Alertas' },
    { path: 'history', label: 'Históricos' },
  ] as const;
}
