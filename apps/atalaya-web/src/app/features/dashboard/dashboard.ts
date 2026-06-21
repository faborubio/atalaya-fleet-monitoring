import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="feature">
      <h2>Mapa en vivo</h2>
      <p>
        Camino caliente: posiciones de la flota en tiempo real vía SignalR, render
        por lote (OnPush + Signals + coalescencia, ADR-010).
      </p>
      <p class="feature__status">Fase 1 — pendiente de implementación.</p>
    </section>
  `,
  styleUrl: '../feature.scss',
})
export class Dashboard {}
