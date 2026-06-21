import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-alerts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="feature">
      <h2>Alertas</h2>
      <p>
        Alertas por umbral disparadas en los workers y notificadas en vivo
        (read model <code>alerts</code>). Reglas, no ML (SAD §1.3).
      </p>
      <p class="feature__status">Fase 2 — pendiente de implementación.</p>
    </section>
  `,
  styleUrl: '../feature.scss',
})
export class Alerts {}
