import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { AlertStore } from '../../core/telemetry/alert-store';

/**
 * Feature de alertas (Fase 2): lista en vivo de las alertas por umbral disparadas en el
 * worker (read model `alerts`, ADR-005) y notificadas por SignalR (ADR-002). OnPush + signals.
 */
@Component({
  selector: 'app-alerts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe],
  template: `
    <section class="feature">
      <h2>Alertas</h2>
      <p>
        Alertas por umbral disparadas en los workers y notificadas en vivo
        (read model <code>alerts</code>). Reglas, no ML (SAD §1.3).
      </p>

      <div class="alerts__counts">
        <span class="pill pill--critical">{{ store.criticalCount() }} críticas</span>
        <span class="pill pill--warning">{{ store.warningCount() }} aviso</span>
        <span class="pill">{{ store.total() }} recientes</span>
      </div>

      @if (store.alerts().length === 0) {
        <p class="feature__status">Sin alertas todavía. Genera carga para verlas en vivo.</p>
      } @else {
        <table class="alerts__table">
          <thead>
            <tr>
              <th>Severidad</th>
              <th>Dispositivo</th>
              <th>Regla</th>
              <th>Valor</th>
              <th>Hora</th>
              <th>Detalle</th>
            </tr>
          </thead>
          <tbody>
            @for (a of store.alerts(); track a.alertId) {
              <tr>
                <td>
                  <span
                    class="pill"
                    [class.pill--critical]="a.severity === 'Critical'"
                    [class.pill--warning]="a.severity === 'Warning'"
                    >{{ a.severity === 'Critical' ? 'Crítica' : 'Aviso' }}</span
                  >
                </td>
                <td>{{ a.deviceId }}</td>
                <td><code>{{ a.rule }}</code></td>
                <td>{{ a.value | number: '1.0-1' }}</td>
                <td>{{ a.ts | date: 'HH:mm:ss' }}</td>
                <td>{{ a.message }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
  styleUrls: ['../feature.scss', './alerts.scss'],
})
export class Alerts {
  protected readonly store = inject(AlertStore);
}
