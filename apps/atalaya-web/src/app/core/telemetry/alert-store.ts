import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { API_CONFIG } from '../api.config';
import { Alert } from '../models/alert';
import { TelemetryStreamService } from './telemetry-stream.service';

/**
 * Estado de alertas en cliente (Fase 2). Igual que el FleetStore vive fuera del NgRx Store
 * (ADR-003), pero las alertas son de bajo volumen: no necesitan coalescencia. Mantiene las
 * más recientes (acotado), deduplica por `alertId` (snapshot + vivo) y expone conteos por
 * severidad para los badges del shell.
 */
@Injectable({ providedIn: 'root' })
export class AlertStore {
  private static readonly MAX = 200;

  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly stream = inject(TelemetryStreamService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly seen = new Set<string>();
  private started = false;

  /** Alertas recientes (las más nuevas primero). */
  readonly alerts = signal<Alert[]>([]);

  readonly total = computed(() => this.alerts().length);
  readonly criticalCount = computed(
    () => this.alerts().filter((a) => a.severity === 'Critical').length
  );
  readonly warningCount = computed(
    () => this.alerts().filter((a) => a.severity === 'Warning').length
  );

  /** Idempotente: se engancha al stream una sola vez (llamado desde el App root). */
  start(): void {
    if (this.started) return;
    this.started = true;

    // Re-snapshot del read model en cada (re)conexión: cierra huecos tras una caída.
    this.stream.connected$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadSnapshot());

    // Alertas en vivo por SignalR.
    this.stream.alerts$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((batch) => this.prepend(batch));
  }

  private loadSnapshot(): void {
    this.http
      .get<Alert[]>(`${this.config.baseUrl}/api/alerts`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (snapshot) => this.prepend(snapshot),
        error: () => void 0, // la API puede no estar lista; el próximo connect reintenta
      });
  }

  private prepend(incoming: Alert[]): void {
    const fresh = incoming.filter((a) => !this.seen.has(a.alertId));
    if (fresh.length === 0) return;
    for (const a of fresh) this.seen.add(a.alertId);

    // Más nuevas primero (por ts); acota la lista y el set de dedup.
    const merged = [...fresh, ...this.alerts()]
      .sort((a, b) => Date.parse(b.ts) - Date.parse(a.ts))
      .slice(0, AlertStore.MAX);

    this.seen.clear();
    for (const a of merged) this.seen.add(a.alertId);
    this.alerts.set(merged);
  }
}
