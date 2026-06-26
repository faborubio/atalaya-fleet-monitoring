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
  // Anti split-brain (AUDIT §7.16): si un dispositivo visible no recibe deltas en STALE_MS (push
  // perdido por crash del worker entre el ACK y el envío a SignalR), se fuerza un refresco silencioso
  // del read model. STALE_CHECK_MS = cadencia del barrido.
  private static readonly STALE_MS = 30_000;
  private static readonly STALE_CHECK_MS = 5_000;

  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly stream = inject(TelemetryStreamService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly byId = new Map<string, DeviceState>();
  private readonly lastSeen = new Map<string, number>(); // último delta en vivo por dispositivo (ms)
  private readonly refreshedStale = new Set<string>(); // ya refrescados en este episodio de stale
  private viewport: string[] | null = null; // ids visibles (null = firehose)
  private wired = false; // subscripciones al stream: una sola vez por vida de la app
  private connected = false; // ciclo de conexión del hub (toggle con login/logout, G3)
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

  /** Arranca el hub (idempotente). Cablea las subscripciones la primera vez; reconecta tras un stop. */
  async start(): Promise<void> {
    this.wire();
    if (this.connected) return;
    this.connected = true;
    await this.stream.connect();
  }

  /**
   * Cierra el stream y limpia el estado en cliente (al cerrar sesión, G3). Las subscripciones
   * quedan vivas pero ociosas (el stream no emite desconectado); un `start()` posterior reconecta
   * sin re-subscribir.
   */
  async stop(): Promise<void> {
    if (!this.connected) return;
    this.connected = false;
    await this.stream.disconnect();
    this.byId.clear();
    this.lastSeen.clear();
    this.refreshedStale.clear();
    this.devices.set([]);
    this.eventsPerSec.set(0);
    this.latencies = [];
  }

  /** Cablea snapshot + stream + tick una sola vez (independiente del ciclo de conexión). */
  private wire(): void {
    if (this.wired) return;
    this.wired = true;

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

    // Barrido de datos obsoletos (AUDIT §7.16): re-sincroniza desde el read model si un dispositivo
    // visible dejó de recibir deltas (push perdido), sin esperar a su próximo evento.
    const staleTick = setInterval(() => this.checkStale(), FleetStore.STALE_CHECK_MS);
    this.destroyRef.onDestroy(() => clearInterval(staleTick));
  }

  /**
   * Modo viewport (AUD-008): limita el push en vivo a `deviceIds` (los visibles). `null` vuelve
   * al firehose. Los dispositivos fuera del viewport conservan su último estado (se "congelan").
   */
  setViewport(deviceIds: string[] | null): void {
    this.viewport = deviceIds;
    void this.stream.setViewport(deviceIds);
  }

  private loadSnapshot(): void {
    this.http
      .get<DeviceState[]>(`${this.config.baseUrl}/api/devices`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (snapshot) => {
          const now = Date.now();
          for (const d of snapshot) {
            this.byId.set(d.deviceId, d);
            // Baseline de "visto" solo para dispositivos nuevos: da una ventana de gracia antes de
            // considerarlos obsoletos. No pisa el lastSeen de un delta en vivo previo.
            if (!this.lastSeen.has(d.deviceId)) this.lastSeen.set(d.deviceId, now);
          }
          this.devices.set([...this.byId.values()]);
        },
        error: () => void 0, // la API puede no estar lista; el próximo connect reintenta
      });
  }

  /**
   * Detecta dispositivos visibles sin deltas recientes y dispara un único refresco silencioso por
   * episodio (AUDIT §7.16). En modo viewport solo vigila los visibles (los demás se "congelan" a
   * propósito, AUD-008). El dedup por `refreshedStale` evita martillar la API si el silencio persiste.
   */
  private checkStale(): void {
    if (!this.live()) return;
    const now = Date.now();
    const candidates = this.viewport ?? [...this.byId.keys()];
    let foundNew = false;
    for (const id of candidates) {
      const seen = this.lastSeen.get(id);
      if (seen !== undefined && now - seen > FleetStore.STALE_MS && !this.refreshedStale.has(id)) {
        this.refreshedStale.add(id);
        foundNew = true;
      }
    }
    if (foundNew) this.loadSnapshot();
  }

  private applyWindows(windows: DeviceState[][]): void {
    let applied = 0;
    const now = Date.now();
    for (const batch of windows) {
      for (const d of batch) {
        const current = this.byId.get(d.deviceId);
        if (!current || d.seq >= current.seq) {
          this.byId.set(d.deviceId, d);
          this.lastSeen.set(d.deviceId, now); // delta en vivo recibido (AUDIT §7.16)
          this.refreshedStale.delete(d.deviceId); // sale del episodio de stale
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
