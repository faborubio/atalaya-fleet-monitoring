import {
  DestroyRef,
  Injectable,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { HubConnectionState } from '@microsoft/signalr';
import { bufferTime, filter } from 'rxjs';
import { API_CONFIG } from '../api.config';
import { DeviceState } from '../models/device-state';
import { TelemetryStreamService } from './telemetry-stream.service';

/**
 * Estado del camino caliente (ADR-003): el firehose de telemetría NO entra al NgRx Store
 * global; vive aquí, en una capa reactiva dedicada con signals.
 *
 * Coalescencia (ADR-010): los deltas que llegan a miles/seg se agrupan por ventana y se
 * aplican en un solo `set` del signal — un render por ventana, no por evento. Es lo que
 * mantiene la UI fluida bajo carga.
 */
@Injectable({ providedIn: 'root' })
export class FleetStore {
  private static readonly COALESCE_MS = 100;

  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly stream = inject(TelemetryStreamService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly byId = new Map<string, DeviceState>();
  private started = false;
  private appliedThisSecond = 0;

  /** Snapshot actual de la flota (read model en cliente). */
  readonly devices = signal<DeviceState[]>([]);
  /** Throughput aplicado (eventos/seg). */
  readonly eventsPerSec = signal(0);
  /** Latencia evento→pantalla del último segundo (NFR estrella del SAD §2/§9), en ms. */
  readonly latencyP50 = signal(0);
  readonly latencyP95 = signal(0);

  private latencies: number[] = [];

  readonly count = computed(() => this.devices().length);
  readonly status = this.stream.status;
  readonly live = computed(() => this.status() === HubConnectionState.Connected);

  /** Idempotente: arranca snapshot + stream una sola vez (llamado desde el App root). */
  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;

    // Re-sincroniza el snapshot del read model en cada (re)conexión: cierra huecos tras
    // una caída sin necesidad de replay por evento (el read model ya es el estado actual).
    this.stream.connected$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadSnapshot());

    // Stream en vivo con coalescencia por ventana.
    this.stream.deltas$
      .pipe(
        bufferTime(FleetStore.COALESCE_MS),
        filter((windows) => windows.length > 0),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((windows) => this.applyWindows(windows));

    const tick = setInterval(() => {
      this.eventsPerSec.set(this.appliedThisSecond);
      this.appliedThisSecond = 0;
      this.latencyP50.set(percentile(this.latencies, 50));
      this.latencyP95.set(percentile(this.latencies, 95));
      this.latencies = [];
    }, 1000);
    this.destroyRef.onDestroy(() => clearInterval(tick));

    await this.stream.connect();
  }

  private loadSnapshot(): void {
    this.http
      .get<DeviceState[]>(`${this.config.baseUrl}/api/devices`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (snapshot) => {
          for (const d of snapshot) this.byId.set(d.deviceId, d);
          this.devices.set([...this.byId.values()]);
        },
        error: () => void 0, // la API puede no estar lista; el próximo connect reintenta
      });
  }

  private applyWindows(windows: DeviceState[][]): void {
    let applied = 0;
    const now = Date.now();
    for (const batch of windows) {
      for (const d of batch) {
        const current = this.byId.get(d.deviceId);
        if (!current || d.seq >= current.seq) {
          this.byId.set(d.deviceId, d);
          this.latencies.push(now - Date.parse(d.ts)); // evento→aplicación en cliente
          applied++;
        }
      }
    }
    this.appliedThisSecond += applied;
    // Un único set por ventana ⇒ una sola pasada de change detection (OnPush).
    this.devices.set([...this.byId.values()]);
  }
}

/** Percentil simple (p en 0..100) sobre una muestra; 0 si está vacía. */
function percentile(samples: number[], p: number): number {
  if (samples.length === 0) return 0;
  const sorted = [...samples].sort((a, b) => a - b);
  const idx = Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length));
  return Math.round(sorted[idx]);
}
