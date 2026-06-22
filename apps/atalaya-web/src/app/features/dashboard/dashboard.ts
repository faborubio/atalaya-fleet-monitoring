import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FleetStore } from '../../core/telemetry/fleet-store';
import { DeviceState } from '../../core/models/device-state';

interface Bounds {
  minLat: number;
  maxLat: number;
  minLng: number;
  maxLng: number;
}

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="dash">
      <header class="dash__head">
        <h2>Mapa en vivo</h2>
        <div class="stats">
          <div class="stat">
            <span class="stat__val">{{ fleet.count() }}</span>
            <span class="stat__lbl">dispositivos</span>
          </div>
          <div class="stat">
            <span class="stat__val">{{ fleet.eventsPerSec() }}</span>
            <span class="stat__lbl">eventos/seg</span>
          </div>
          <div class="stat" [class.is-warn]="fleet.latencyP95() > 1500">
            <span class="stat__val">{{ fleet.latencyP95() }}<small>ms</small></span>
            <span class="stat__lbl">latencia P95 (NFR &lt; 1500)</span>
          </div>
          <div class="stat" [class.is-live]="fleet.live()">
            <span class="stat__val">{{ fleet.live() ? '●' : '○' }}</span>
            <span class="stat__lbl">{{ fleet.live() ? 'en vivo' : 'sin conexión' }}</span>
          </div>
        </div>
      </header>

      <div class="dash__viewport">
        <span class="dash__vp-lbl">Viewport (AUD-008):</span>
        @for (z of zooms; track z.factor) {
          <button
            type="button"
            class="dash__vp-btn"
            [class.is-active]="zoom() === z.factor"
            (click)="zoom.set(z.factor)"
          >
            {{ z.label }}
          </button>
        }
        @if (visibleIds(); as v) {
          <span class="dash__vp-info">
            suscrito a {{ v.size }} de {{ fleet.count() }} — el resto se congela
          </span>
        } @else {
          <span class="dash__vp-info">firehose (todos los dispositivos en vivo)</span>
        }
      </div>

      @if (fleet.count() === 0) {
        <p class="dash__hint">
          Esperando telemetría. Arranca la API (<code>nx serve api</code>) y el simulador
          apuntando a <code>/ingest</code>.
        </p>
      }

      <canvas #canvas class="dash__canvas"></canvas>
    </section>
  `,
  styleUrl: './dashboard.scss',
})
export class Dashboard implements AfterViewInit {
  protected readonly fleet = inject(FleetStore);
  private readonly canvasRef =
    viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');

  protected readonly zooms = [
    { factor: 1, label: 'Todo' },
    { factor: 2, label: '2×' },
    { factor: 4, label: '4×' },
  ] as const;

  /** Factor de zoom del viewport: 1 = toda la flota (firehose); >1 = recorte central. */
  protected readonly zoom = signal(1);

  private readonly bounds = computed<Bounds | null>(() => {
    const devices = this.fleet.devices();
    if (devices.length === 0) return null;
    let minLat = Infinity, maxLat = -Infinity, minLng = Infinity, maxLng = -Infinity;
    for (const d of devices) {
      minLat = Math.min(minLat, d.lat);
      maxLat = Math.max(maxLat, d.lat);
      minLng = Math.min(minLng, d.lng);
      maxLng = Math.max(maxLng, d.lng);
    }
    return { minLat, maxLat, minLng, maxLng };
  });

  /** Ids visibles según el zoom (recorte central). `null` = todos (firehose). */
  protected readonly visibleIds = computed<Set<string> | null>(() => {
    const z = this.zoom();
    const b = this.bounds();
    if (z === 1 || !b) return null;

    const cLat = (b.minLat + b.maxLat) / 2;
    const cLng = (b.minLng + b.maxLng) / 2;
    const halfLat = (b.maxLat - b.minLat) / 2 / z;
    const halfLng = (b.maxLng - b.minLng) / 2 / z;

    const ids = new Set<string>();
    for (const d of this.fleet.devices())
      if (Math.abs(d.lat - cLat) <= halfLat && Math.abs(d.lng - cLng) <= halfLng)
        ids.add(d.deviceId);
    return ids;
  });

  private lastViewportSig = '';

  constructor() {
    // Redibuja cuando cambia el snapshot coalescido (un repaint por ventana).
    effect(() => {
      const devices = this.fleet.devices();
      const visible = this.visibleIds();
      const canvas = this.canvasRef();
      if (canvas) this.draw(canvas.nativeElement, devices, visible);
    });

    // Sincroniza el viewport con el servidor solo cuando cambia el conjunto (evita spam).
    effect(() => {
      const ids = this.visibleIds();
      const sig = ids === null ? 'ALL' : [...ids].sort().join(',');
      if (sig === this.lastViewportSig) return;
      this.lastViewportSig = sig;
      this.fleet.setViewport(ids === null ? null : [...ids]);
    });

    // Al salir del dashboard, vuelve al firehose para el resto de la app.
    inject(DestroyRef).onDestroy(() => this.fleet.setViewport(null));
  }

  ngAfterViewInit(): void {
    this.resizeToParent();
  }

  private resizeToParent(): void {
    const canvas = this.canvasRef().nativeElement;
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.floor(rect.width * dpr));
    canvas.height = Math.max(1, Math.floor(rect.height * dpr));
    this.draw(canvas, this.fleet.devices(), this.visibleIds());
  }

  private draw(
    canvas: HTMLCanvasElement,
    devices: DeviceState[],
    visible: Set<string> | null
  ): void {
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const { width: w, height: h } = canvas;

    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = '#0d1117';
    ctx.fillRect(0, 0, w, h);
    if (devices.length === 0) return;

    let minLat = Infinity, maxLat = -Infinity, minLng = Infinity, maxLng = -Infinity;
    for (const d of devices) {
      minLat = Math.min(minLat, d.lat);
      maxLat = Math.max(maxLat, d.lat);
      minLng = Math.min(minLng, d.lng);
      maxLng = Math.max(maxLng, d.lng);
    }
    const pad = 24;
    const spanLat = maxLat - minLat || 1;
    const spanLng = maxLng - minLng || 1;

    for (const d of devices) {
      const x = pad + ((d.lng - minLng) / spanLng) * (w - 2 * pad);
      // lat invertida: norte arriba
      const y = pad + ((maxLat - d.lat) / spanLat) * (h - 2 * pad);
      const inViewport = visible === null || visible.has(d.deviceId);
      const t = Math.min(1, d.speedKmh / 120);
      // Fuera del viewport: gris atenuado (congelado, no recibe deltas en vivo).
      ctx.fillStyle = inViewport
        ? `rgb(${Math.round(60 + t * 180)}, ${Math.round(190 - t * 120)}, 80)`
        : 'rgba(110, 118, 129, 0.35)';
      ctx.beginPath();
      ctx.arc(x, y, inViewport ? 3 : 2, 0, Math.PI * 2);
      ctx.fill();
    }
  }
}
