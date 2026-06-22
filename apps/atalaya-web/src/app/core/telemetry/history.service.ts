import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_CONFIG } from '../api.config';
import { TelemetryEvent } from '../models/telemetry-event';

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
}
