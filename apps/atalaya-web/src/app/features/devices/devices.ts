import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-devices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="feature">
      <h2>Dispositivos</h2>
      <p>
        Catálogo y estado por dispositivo (read model <code>device_state</code>).
        Listas con CDK Virtual Scroll para sostener cientos de activos sin jank.
      </p>
      <p class="feature__status">Fase 1 — pendiente de implementación.</p>
    </section>
  `,
  styleUrl: '../feature.scss',
})
export class Devices {}
