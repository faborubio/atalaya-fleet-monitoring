import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { AlertStore } from '../../core/telemetry/alert-store';

/**
 * Feature de alertas como incidentes (AUD-016/p1): cada fila es un incidente por
 * `(dispositivo, regla)` con estado abierto/resuelto e histéresis (no una alerta por evento).
 * OnPush + signals; los conteos cuentan solo incidentes abiertos.
 */
@Component({
  selector: 'app-alerts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe],
  template: `
    <section class="feature">
      <h2>Alertas</h2>
      <p>
        Incidentes por umbral con <strong>histéresis</strong>: un incidente por
        <code>(dispositivo, regla)</code> que abre, escala y se resuelve. Solo las transiciones se
        notifican (AUD-016). Reglas, no ML (SAD §1.3).
      </p>

      <div class="alerts__counts">
        <span class="pill pill--critical">{{ store.criticalCount() }} críticas abiertas</span>
        <span class="pill pill--warning">{{ store.warningCount() }} aviso abiertas</span>
        <span class="pill">{{ store.openCount() }} abiertas / {{ store.incidents().length }} totales</span>
      </div>

      @if (store.incidents().length === 0) {
        <p class="feature__status">Sin incidentes todavía. Genera carga para verlos en vivo.</p>
      } @else {
        <table class="alerts__table">
          <thead>
            <tr>
              <th>Estado</th>
              <th>Severidad</th>
              <th>Dispositivo</th>
              <th>Regla</th>
              <th>Valor</th>
              <th>Abierto</th>
              <th>Actualizado</th>
              <th>Detalle</th>
            </tr>
          </thead>
          <tbody>
            @for (i of store.incidents(); track i.incidentId) {
              <tr [class.is-resolved]="i.status === 'Resolved'">
                <td>
                  <span class="pill" [class.pill--open]="i.status === 'Open'">
                    {{ i.status === 'Open' ? 'Abierta' : 'Resuelta' }}
                  </span>
                </td>
                <td>
                  <span
                    class="pill"
                    [class.pill--critical]="i.severity === 'Critical'"
                    [class.pill--warning]="i.severity === 'Warning'"
                    >{{ i.severity === 'Critical' ? 'Crítica' : 'Aviso' }}</span
                  >
                </td>
                <td>{{ i.deviceId }}</td>
                <td><code>{{ i.rule }}</code></td>
                <td>{{ i.value | number: '1.0-1' }}</td>
                <td>{{ i.openedAt | date: 'HH:mm:ss' }}</td>
                <td>{{ i.updatedAt | date: 'HH:mm:ss' }}</td>
                <td>{{ i.message }}</td>
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
