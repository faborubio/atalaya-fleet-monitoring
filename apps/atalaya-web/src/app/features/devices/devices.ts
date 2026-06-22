import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FleetStore } from '../../core/telemetry/fleet-store';

@Component({
  selector: 'app-devices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe],
  template: `
    <section class="feature">
      <h2>Dispositivos <span class="count">({{ fleet.count() }})</span></h2>
      <p class="muted">
        Read model <code>device_state</code> en vivo. Las filas se reusan por
        <code>trackBy</code>; el store coalesce el firehose antes de tocar la vista.
      </p>

      @if (fleet.count() === 0) {
        <p class="muted">Sin dispositivos todavía.</p>
      } @else {
        <table class="grid">
          <thead>
            <tr>
              <th>Dispositivo</th><th>seq</th><th>Lat</th><th>Lng</th>
              <th>km/h</th><th>Combustible</th><th>Motor °C</th>
            </tr>
          </thead>
          <tbody>
            @for (d of rows(); track d.deviceId) {
              <tr>
                <td>{{ d.deviceId }}</td>
                <td>{{ d.seq }}</td>
                <td>{{ d.lat | number: '1.4-4' }}</td>
                <td>{{ d.lng | number: '1.4-4' }}</td>
                <td>{{ d.speedKmh | number: '1.0-0' }}</td>
                <td>{{ d.fuelPct | number: '1.0-0' }}%</td>
                <td>{{ d.engineTempC | number: '1.0-0' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
  styleUrl: './devices.scss',
})
export class Devices {
  protected readonly fleet = inject(FleetStore);

  /** Ordenado por id y acotado; CDK Virtual Scroll queda como mejora (SAD §9). */
  protected readonly rows = computed(() =>
    [...this.fleet.devices()]
      .sort((a, b) => a.deviceId.localeCompare(b.deviceId))
      .slice(0, 200)
  );
}
