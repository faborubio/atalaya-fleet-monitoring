import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FleetStore } from '../../core/telemetry/fleet-store';
import { HistoryService } from '../../core/telemetry/history.service';
import { TelemetryBucket } from '../../core/models/telemetry-bucket';

type MetricKey = 'engineTempC' | 'speedKmh' | 'fuelPct';

const METRICS: { key: MetricKey; label: string }[] = [
  { key: 'engineTempC', label: 'Motor °C' },
  { key: 'speedKmh', label: 'Velocidad km/h' },
  { key: 'fuelPct', label: 'Combustible %' },
];

/**
 * Feature de históricos (Fase 2, camino frío — ADR-005/007). Consulta puntual a
 * `/api/history` (telemetría particionada por tiempo) y dibuja una serie temporal del
 * métrico elegido. No usa el stream en vivo: el histórico nunca compite con el firehose.
 */
@Component({
  selector: 'app-history',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe],
  template: `
    <section class="feature">
      <h2>Históricos</h2>
      <p class="muted">
        Camino frío: serie temporal por dispositivo desde la telemetría
        <strong>particionada por tiempo</strong> (ADR-007), <strong>downsampled</strong> a ~200 puntos
        (promedio por intervalo) para que los rangos largos no traigan miles de filas.
      </p>

      <form class="history__controls" (submit)="$event.preventDefault(); load()">
        <label>
          Dispositivo
          <input
            list="history-devices"
            [value]="deviceId()"
            (input)="deviceId.set($any($event.target).value)"
            placeholder="dev-00001"
          />
          <datalist id="history-devices">
            @for (id of devices(); track id) {
              <option [value]="id"></option>
            }
          </datalist>
        </label>

        <label>
          Rango
          <select (change)="minutes.set(+$any($event.target).value)">
            <option value="15">15 min</option>
            <option value="60" selected>1 h</option>
            <option value="180">3 h</option>
            <option value="360">6 h</option>
            <option value="1440">24 h</option>
          </select>
        </label>

        <label>
          Métrico
          <select (change)="metric.set($any($event.target).value)">
            @for (m of metrics; track m.key) {
              <option [value]="m.key">{{ m.label }}</option>
            }
          </select>
        </label>

        <button type="submit" [disabled]="loading()">
          {{ loading() ? 'Consultando…' : 'Consultar' }}
        </button>
      </form>

      @if (error()) {
        <p class="feature__status">{{ error() }}</p>
      } @else if (points().length === 0) {
        <p class="muted">Sin datos. Elige un dispositivo con telemetría reciente y consulta.</p>
      } @else {
        <div class="history__summary">
          <span class="pill">{{ points().length }} puntos (agregados)</span>
          <span class="pill">mín {{ stats().min | number: '1.0-1' }}</span>
          <span class="pill">máx {{ stats().max | number: '1.0-1' }}</span>
          <span class="pill">prom {{ stats().avg | number: '1.0-1' }}</span>
        </div>

        @if (chart(); as c) {
          <svg class="history__chart" [attr.viewBox]="'0 0 ' + c.w + ' ' + c.h" preserveAspectRatio="none">
            <polyline [attr.points]="c.polyline" />
          </svg>
        }

        <table class="history__table">
          <thead>
            <tr><th>Intervalo</th><th>Motor °C</th><th>km/h</th><th>Combustible</th><th>n</th></tr>
          </thead>
          <tbody>
            @for (p of points().slice(0, 30); track p.ts) {
              <tr>
                <td>{{ p.ts | date: 'HH:mm:ss' }}</td>
                <td>{{ p.engineTempC | number: '1.0-1' }}</td>
                <td>{{ p.speedKmh | number: '1.0-0' }}</td>
                <td>{{ p.fuelPct | number: '1.0-0' }}%</td>
                <td>{{ p.count }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
  styleUrls: ['../feature.scss', './history.scss'],
})
export class History {
  private readonly fleet = inject(FleetStore);
  private readonly history = inject(HistoryService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly metrics = METRICS;

  protected readonly deviceId = signal('');
  protected readonly minutes = signal(60);
  protected readonly metric = signal<MetricKey>('engineTempC');

  protected readonly points = signal<TelemetryBucket[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Dispositivos conocidos (del read model en vivo) para autocompletar el selector. */
  protected readonly devices = computed(() =>
    [...this.fleet.devices()].map((d) => d.deviceId).sort()
  );

  constructor() {
    // Prefija el primer dispositivo en cuanto haya snapshot, sin pisar la elección del usuario.
    effect(() => {
      const ids = this.devices();
      if (!this.deviceId() && ids.length > 0) this.deviceId.set(ids[0]);
    });
  }

  protected readonly stats = computed(() => {
    const key = this.metric();
    const values = this.points().map((p) => p[key]);
    if (values.length === 0) return { min: 0, max: 0, avg: 0 };
    const sum = values.reduce((a, b) => a + b, 0);
    return { min: Math.min(...values), max: Math.max(...values), avg: sum / values.length };
  });

  /** Polilínea SVG de la serie (ascendente en el tiempo), escalada al viewBox. */
  protected readonly chart = computed(() => {
    const key = this.metric();
    const pts = [...this.points()].sort((a, b) => Date.parse(a.ts) - Date.parse(b.ts));
    if (pts.length < 2) return null;

    const w = 600;
    const h = 180;
    const pad = 24;
    const xs = pts.map((p) => Date.parse(p.ts));
    const ys = pts.map((p) => p[key]);
    const xMin = xs[0];
    const xSpan = xs[xs.length - 1] - xMin || 1;
    const yMin = Math.min(...ys);
    const ySpan = Math.max(...ys) - yMin || 1;

    const polyline = pts
      .map((_, i) => {
        const x = pad + ((xs[i] - xMin) / xSpan) * (w - 2 * pad);
        const y = h - pad - ((ys[i] - yMin) / ySpan) * (h - 2 * pad);
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');

    return { w, h, polyline };
  });

  protected load(): void {
    const id = this.deviceId().trim();
    if (!id) {
      this.error.set('Indica un dispositivo.');
      return;
    }
    this.loading.set(true);
    this.error.set(null);

    this.history
      .series(id, this.minutes())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (pts) => {
          this.points.set(pts);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('No se pudo consultar el histórico.');
          this.loading.set(false);
        },
      });
  }
}
