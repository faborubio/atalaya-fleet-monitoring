import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { FleetStore } from '../../core/telemetry/fleet-store';
import { DeviceState } from '../../core/models/device-state';

@Component({
  selector: 'app-devices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, ScrollingModule],
  template: `
    <section class="feature">
      <h2>Dispositivos <span class="count">({{ fleet.count() }})</span></h2>
      <p class="muted">
        Read model <code>device_state</code> en vivo. <strong>Virtual scroll</strong> (CDK): solo se
        renderizan las filas visibles, así la tabla escala a miles sin coste de DOM; el store coalesce
        el firehose antes de tocar la vista.
      </p>

      @if (fleet.count() === 0) {
        <p class="muted">Sin dispositivos todavía.</p>
      } @else {
        <div class="vgrid">
          <div class="vgrid__row vgrid__head">
            <span>Dispositivo</span><span>seq</span><span>Lat</span><span>Lng</span>
            <span>km/h</span><span>Combustible</span><span>Motor °C</span>
          </div>
          <cdk-virtual-scroll-viewport [itemSize]="32" class="vgrid__vp">
            <div
              class="vgrid__row"
              *cdkVirtualFor="let d of rows(); trackBy: trackById"
            >
              <span class="vgrid__id">{{ d.deviceId }}</span>
              <span>{{ d.seq }}</span>
              <span>{{ d.lat | number: '1.4-4' }}</span>
              <span>{{ d.lng | number: '1.4-4' }}</span>
              <span>{{ d.speedKmh | number: '1.0-0' }}</span>
              <span>{{ d.fuelPct | number: '1.0-0' }}%</span>
              <span>{{ d.engineTempC | number: '1.0-0' }}</span>
            </div>
          </cdk-virtual-scroll-viewport>
        </div>
      }
    </section>
  `,
  styleUrl: './devices.scss',
})
export class Devices {
  protected readonly fleet = inject(FleetStore);

  /** Ordenado por id; sin recorte — el virtual scroll renderiza solo lo visible (SAD §9). */
  protected readonly rows = computed(() =>
    [...this.fleet.devices()].sort((a, b) => a.deviceId.localeCompare(b.deviceId))
  );

  protected readonly trackById = (_: number, d: DeviceState) => d.deviceId;
}
