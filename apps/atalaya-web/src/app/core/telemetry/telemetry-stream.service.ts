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
import { Alert } from '../models/alert';

/**
 * Capa de transporte del camino caliente (ADR-002): mantiene la conexión SignalR con
 * reconexión automática y publica los deltas entrantes como un stream RxJS. No mantiene
 * estado de dominio — de eso se encarga el FleetStore (ADR-003).
 */
@Injectable({ providedIn: 'root' })
export class TelemetryStreamService {
  private readonly config = inject(API_CONFIG);
  private readonly deltas = new Subject<DeviceState[]>();
  private readonly alerts = new Subject<Alert[]>();
  private readonly connected = new Subject<void>();
  private connection?: HubConnection;

  /** Estado de conexión, para que la UI muestre "en vivo / reconectando". */
  readonly status = signal<HubConnectionState>(HubConnectionState.Disconnected);

  /** Lote de deltas tal como llegan del hub (`devicesUpdated`). */
  readonly deltas$: Observable<DeviceState[]> = this.deltas.asObservable();

  /** Alertas nuevas tal como llegan del hub (`alertsRaised`). */
  readonly alerts$: Observable<Alert[]> = this.alerts.asObservable();

  /**
   * Emite en cada (re)conexión exitosa. El store lo usa para re-sincronizar el snapshot
   * y así no quedar con huecos tras una caída (ADR-006).
   */
  readonly connected$: Observable<void> = this.connected.asObservable();

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

    this.connection.on('alertsRaised', (batch: Alert[]) =>
      this.alerts.next(batch)
    );

    this.connection.onreconnecting(() => this.status.set(HubConnectionState.Reconnecting));
    this.connection.onreconnected(() => {
      this.status.set(HubConnectionState.Connected);
      this.connected.next(); // re-sincroniza el snapshot tras reconectar
    });
    this.connection.onclose(() => this.status.set(HubConnectionState.Disconnected));

    try {
      await this.connection.start();
      this.status.set(HubConnectionState.Connected);
      this.connected.next();
    } catch {
      this.status.set(HubConnectionState.Disconnected);
    }
  }
}
