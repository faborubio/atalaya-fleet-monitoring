import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { API_CONFIG } from '../api.config';
import { AlertIncident } from '../models/alert';
import { TelemetryStreamService } from './telemetry-stream.service';

/**
 * Estado de incidentes de alerta en cliente (AUD-016/p1). A diferencia del modelo por-evento,
 * indexa por `incidentId` y aplica transiciones (abrir/escalar/resolver): un incidente que se
 * resuelve actualiza su fila en vez de añadir ruido. Vive fuera del NgRx Store (ADR-003).
 * Los conteos del badge cuentan solo incidentes **abiertos**.
 */
@Injectable({ providedIn: 'root' })
export class AlertStore {
  private static readonly MAX = 200;

  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly stream = inject(TelemetryStreamService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly byId = new Map<string, AlertIncident>();
  private started = false;

  /** Incidentes (abiertos primero, luego resueltos), más recientes arriba. */
  readonly incidents = signal<AlertIncident[]>([]);

  readonly openCount = computed(() => this.incidents().filter((i) => i.status === 'Open').length);
  readonly criticalCount = computed(
    () => this.incidents().filter((i) => i.status === 'Open' && i.severity === 'Critical').length
  );
  readonly warningCount = computed(
    () => this.incidents().filter((i) => i.status === 'Open' && i.severity === 'Warning').length
  );

  /** Idempotente: se engancha al stream una sola vez (llamado desde el App root). */
  start(): void {
    if (this.started) return;
    this.started = true;

    this.stream.connected$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadSnapshot());

    this.stream.alerts$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((batch) => this.apply(batch));
  }

  private loadSnapshot(): void {
    this.http
      .get<AlertIncident[]>(`${this.config.baseUrl}/api/alerts`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (snapshot) => this.apply(snapshot),
        error: () => void 0,
      });
  }

  /** Upsert por incidentId; reordena (abiertos primero, luego por actualización) y acota. */
  private apply(incidents: AlertIncident[]): void {
    for (const i of incidents) this.byId.set(i.incidentId, i);

    const merged = [...this.byId.values()]
      .sort((a, b) => {
        const open = Number(b.status === 'Open') - Number(a.status === 'Open');
        return open !== 0 ? open : Date.parse(b.updatedAt) - Date.parse(a.updatedAt);
      })
      .slice(0, AlertStore.MAX);

    this.byId.clear();
    for (const i of merged) this.byId.set(i.incidentId, i);
    this.incidents.set(merged);
  }
}
