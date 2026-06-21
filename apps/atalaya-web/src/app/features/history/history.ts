import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-history',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="feature">
      <h2>Históricos</h2>
      <p>
        Camino frío: consultas agregadas sobre telemetría particionada por tiempo
        y S3/Athena. Nunca compite con el camino caliente (ADR-005).
      </p>
      <p class="feature__status">Fase 2 — pendiente de implementación.</p>
    </section>
  `,
  styleUrl: '../feature.scss',
})
export class History {}
