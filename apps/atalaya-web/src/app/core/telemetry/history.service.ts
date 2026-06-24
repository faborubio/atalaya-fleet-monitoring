import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_CONFIG } from '../api.config';
import { TelemetryEvent } from '../models/telemetry-event';
import { TelemetryBucket } from '../models/telemetry-bucket';

/**
 * Cliente del camino frío (ADR-005/007): consulta histórica por dispositivo contra la
 * telemetría particionada. Es REST puntual (no streaming): el histórico no compite con el
 * firehose en vivo.
 */
@Injectable({ providedIn: 'root' })
export class HistoryService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  query(deviceId: string, minutes: number, limit = 2000): Observable<TelemetryEvent[]> {
    const params = new URLSearchParams({
      deviceId,
      minutes: String(minutes),
      limit: String(limit),
    });
    return this.http.get<TelemetryEvent[]>(
      `${this.config.baseUrl}/api/history?${params.toString()}`
    );
  }

  /**
   * Serie histórica downsampled (AUD-028): hasta `buckets` puntos agregados (promedio por intervalo).
   * Para rangos largos no trae miles de filas crudas; el gráfico se queda fluido.
   */
  series(deviceId: string, minutes: number, buckets = 200): Observable<TelemetryBucket[]> {
    const params = new URLSearchParams({
      deviceId,
      minutes: String(minutes),
      buckets: String(buckets),
    });
    return this.http.get<TelemetryBucket[]>(
      `${this.config.baseUrl}/api/history/series?${params.toString()}`
    );
  }
}
