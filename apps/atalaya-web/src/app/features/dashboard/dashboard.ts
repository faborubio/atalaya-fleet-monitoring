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

type RGBA = [number, number, number, number];

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

      <div class="dash__mapwrap">
        <div #map class="dash__map"></div>
        <span class="dash__attrib">© OpenStreetMap contributors</span>
      </div>
    </section>
  `,
  styleUrl: './dashboard.scss',
})
export class Dashboard implements AfterViewInit {
  protected readonly fleet = inject(FleetStore);
  private readonly mapRef = viewChild.required<ElementRef<HTMLDivElement>>('map');

  protected readonly zooms = [
    { factor: 1, label: 'Todo' },
    { factor: 2, label: '2×' },
    { factor: 4, label: '4×' },
  ] as const;

  /** Factor de zoom del viewport: 1 = toda la flota (firehose); >1 = recorte central. */
  protected readonly zoom = signal(1);

  // deck.gl se carga por dynamic-import (pesado → fuera del bundle inicial y de Jest, patrón de G3).
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private deck: any = null;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private deckModule: any = null;

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
  private fitted = false; // ya se centró el mapa sobre datos reales (no el fallback)

  constructor() {
    // Repinta la capa de puntos cuando cambia el snapshot coalescido o el viewport (una pasada por
    // ventana, ADR-010). Si deck aún no está listo, el constructor de Deck ya recibe el estado actual.
    effect(() => {
      const devices = this.fleet.devices();
      const visible = this.visibleIds();
      if (!this.deck) return;
      // Si el mapa se montó sin datos (init antes del snapshot), usó el fallback; al llegar la flota
      // recentra una sola vez sobre sus límites reales (evita quedarse mirando la región equivocada).
      if (!this.fitted && devices.length > 0) {
        this.fitted = true;
        const c = this.initialCenter();
        this.deck.setProps({
          initialViewState: { longitude: c.lng, latitude: c.lat, zoom: c.zoom, pitch: 0, bearing: 0 },
        });
      }
      this.deck.setProps({ layers: this.buildLayers(devices, visible) });
    });

    // Sincroniza el viewport con el servidor solo cuando cambia el conjunto (evita spam).
    effect(() => {
      const ids = this.visibleIds();
      const sig = ids === null ? 'ALL' : [...ids].sort().join(',');
      if (sig === this.lastViewportSig) return;
      this.lastViewportSig = sig;
      this.fleet.setViewport(ids === null ? null : [...ids]);
    });

    // Al salir del dashboard: vuelve al firehose y libera el contexto WebGL de deck.
    inject(DestroyRef).onDestroy(() => {
      this.fleet.setViewport(null);
      this.deck?.finalize();
    });
  }

  async ngAfterViewInit(): Promise<void> {
    const mod = await import('deck.gl');
    this.deckModule = mod;

    const center = this.initialCenter();
    this.deck = new mod.Deck({
      parent: this.mapRef().nativeElement,
      views: new mod.MapView({ repeat: true }),
      initialViewState: {
        longitude: center.lng,
        latitude: center.lat,
        zoom: center.zoom,
        pitch: 0,
        bearing: 0,
      },
      controller: true, // pan + zoom reales
      pickingRadius: 6, // tolerancia de hover (los puntos son pequeños)
      layers: this.buildLayers(this.fleet.devices(), this.visibleIds()),
      getTooltip: ({ object }: { object?: DeviceState }) =>
        object ? this.tooltip(object) : null,
    });
    // Si ya había datos al montar, el centro inicial ya fue sobre la flota; no recentres de nuevo.
    this.fitted = this.bounds() !== null;
  }

  private initialCenter(): { lng: number; lat: number; zoom: number } {
    const b = this.bounds();
    if (!b) return { lng: -70.6489, lat: -33.4789, zoom: 10.5 }; // Santiago de Chile (región de la flota)
    return { lng: (b.minLng + b.maxLng) / 2, lat: (b.minLat + b.maxLat) / 2, zoom: 10.5 };
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private buildLayers(devices: DeviceState[], visible: Set<string> | null): any[] {
    const { ScatterplotLayer, TileLayer, BitmapLayer } = this.deckModule;

    // Basemap real: tiles raster de OpenStreetMap (sin API key). renderSubLayers pinta cada tile.
    const basemap = new TileLayer({
      id: 'osm',
      data: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      minZoom: 0,
      maxZoom: 19,
      tileSize: 256,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      renderSubLayers: (props: any) => {
        const { boundingBox } = props.tile;
        return new BitmapLayer(props, {
          data: undefined,
          image: props.data,
          bounds: [
            boundingBox[0][0], boundingBox[0][1],
            boundingBox[1][0], boundingBox[1][1],
          ],
        });
      },
    });

    // Dispositivos geolocalizados (lng/lat reales). Fuera del viewport = gris atenuado (congelados).
    const dots = new ScatterplotLayer({
      id: 'devices',
      data: devices,
      getPosition: (d: DeviceState) => [d.lng, d.lat],
      getFillColor: (d: DeviceState) => this.colorFor(d, visible),
      getRadius: 5,
      radiusUnits: 'pixels',
      radiusMinPixels: 2,
      radiusMaxPixels: 9,
      stroked: true,
      getLineColor: [13, 17, 23, 255] as RGBA,
      lineWidthMinPixels: 1,
      pickable: true, // habilita la burbuja de info al pasar por encima
      updateTriggers: {
        // Recolorea cuando cambia el conjunto visible (el zoom de viewport).
        getFillColor: visible === null ? 'all' : visible.size,
      },
    });

    return [basemap, dots];
  }

  private colorFor(d: DeviceState, visible: Set<string> | null): RGBA {
    if (visible !== null && !visible.has(d.deviceId)) return [110, 118, 129, 90];
    const t = Math.min(1, d.speedKmh / 120); // verde (lento) → ámbar (rápido)
    return [Math.round(60 + t * 180), Math.round(190 - t * 120), 80, 255];
  }

  // Tipos de activo, derivados de forma determinista del id (estable por dispositivo).
  private static readonly KINDS = [
    { label: 'Vehículo', icon: '🚚' },
    { label: 'Sensor IoT', icon: '📡' },
    { label: 'Maquinaria', icon: '🏭' },
  ] as const;
  private static readonly COMPASS = ['N', 'NE', 'E', 'SE', 'S', 'SO', 'O', 'NO'] as const;

  private kindOf(id: string): { label: string; icon: string } {
    let h = 0;
    for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) | 0;
    return Dashboard.KINDS[Math.abs(h) % Dashboard.KINDS.length];
  }

  private compass(deg: number): string {
    return Dashboard.COMPASS[Math.round(deg / 45) % 8];
  }

  /** Burbuja (deck.gl getTooltip) con la ficha del activo al pasar el cursor por encima. */
  private tooltip(d: DeviceState): { html: string; style: Record<string, string> } {
    const k = this.kindOf(d.deviceId);
    const ageSec = Math.max(0, Math.round((Date.now() - Date.parse(d.ts)) / 1000));
    const row = (lbl: string, val: string, warn = false) =>
      `<div style="display:flex;justify-content:space-between;gap:16px">
         <span style="color:#8b949e">${lbl}</span>
         <span style="color:${warn ? '#ff7b72' : '#e6edf3'};font-variant-numeric:tabular-nums">${val}</span>
       </div>`;
    const html =
      `<div style="font-weight:600;margin-bottom:2px">${k.icon} ${d.deviceId}</div>` +
      `<div style="color:#58a6ff;margin-bottom:6px">${k.label}</div>` +
      row('Velocidad', `${d.speedKmh.toFixed(0)} km/h`) +
      row('Rumbo', `${this.compass(d.headingDeg)} · ${d.headingDeg.toFixed(0)}°`) +
      row('Combustible', `${d.fuelPct.toFixed(0)} %`, d.fuelPct <= 15) +
      row('Motor', `${d.engineTempC.toFixed(0)} °C`, d.engineTempC >= 95) +
      row('Posición', `${d.lat.toFixed(4)}, ${d.lng.toFixed(4)}`) +
      row('Actualizado', ageSec === 0 ? 'ahora' : `hace ${ageSec}s`);
    return {
      html,
      style: {
        background: 'rgba(13,17,23,0.95)',
        border: '1px solid #30363d',
        borderRadius: '8px',
        padding: '10px 12px',
        font: '12px/1.55 system-ui, sans-serif',
        boxShadow: '0 6px 20px rgba(0,0,0,0.5)',
        pointerEvents: 'none',
      },
    };
  }
}
