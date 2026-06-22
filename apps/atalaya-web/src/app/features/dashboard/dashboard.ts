import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { FleetStore } from '../../core/telemetry/fleet-store';
import { DeviceState } from '../../core/models/device-state';

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
          <div class="stat" [class.is-live]="fleet.live()">
            <span class="stat__val">{{ fleet.live() ? '●' : '○' }}</span>
            <span class="stat__lbl">{{ fleet.live() ? 'en vivo' : 'sin conexión' }}</span>
          </div>
        </div>
      </header>

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

  constructor() {
    // Redibuja cuando cambia el snapshot coalescido (un repaint por ventana).
    effect(() => {
      const devices = this.fleet.devices();
      const canvas = this.canvasRef();
      if (canvas) this.draw(canvas.nativeElement, devices);
    });
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
    this.draw(canvas, this.fleet.devices());
  }

  private draw(canvas: HTMLCanvasElement, devices: DeviceState[]): void {
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
      const t = Math.min(1, d.speedKmh / 120);
      ctx.fillStyle = `rgb(${Math.round(60 + t * 180)}, ${Math.round(190 - t * 120)}, 80)`;
      ctx.beginPath();
      ctx.arc(x, y, 3, 0, Math.PI * 2);
      ctx.fill();
    }
  }
}
