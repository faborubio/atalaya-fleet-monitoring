import { Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { API_CONFIG } from '../api.config';
import { DeviceState } from '../models/device-state';

/**
 * Capa de transporte del camino caliente (ADR-002): mantiene la conexión SignalR con
 * reconexión automática y publica los deltas entrantes como un stream RxJS. No mantiene
 * estado de dominio — de eso se encarga el FleetStore (ADR-003).
 */
@Injectable({ providedIn: 'root' })
export class TelemetryStreamService {
  private readonly config = inject(API_CONFIG);
  private readonly deltas = new Subject<DeviceState[]>();
  private connection?: HubConnection;

  /** Estado de conexión, para que la UI muestre "en vivo / reconectando". */
  readonly status = signal<HubConnectionState>(HubConnectionState.Disconnected);

  /** Lote de deltas tal como llegan del hub (`devicesUpdated`). */
  readonly deltas$: Observable<DeviceState[]> = this.deltas.asObservable();

  async connect(): Promise<void> {
    if (this.connection) return;

    this.connection = new HubConnectionBuilder()
      .withUrl(this.config.hubUrl)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('devicesUpdated', (batch: DeviceState[]) =>
      this.deltas.next(batch)
    );

    this.connection.onreconnecting(() => this.status.set(HubConnectionState.Reconnecting));
    this.connection.onreconnected(() => this.status.set(HubConnectionState.Connected));
    this.connection.onclose(() => this.status.set(HubConnectionState.Disconnected));

    try {
      await this.connection.start();
      this.status.set(HubConnectionState.Connected);
    } catch {
      this.status.set(HubConnectionState.Disconnected);
    }
  }
}
