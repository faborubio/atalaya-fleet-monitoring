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
import { AlertIncident } from '../models/alert';

/**
 * Capa de transporte del camino caliente (ADR-002): mantiene la conexión SignalR con
 * reconexión automática y publica los deltas entrantes como un stream RxJS. No mantiene
 * estado de dominio — de eso se encarga el FleetStore (ADR-003).
 */
@Injectable({ providedIn: 'root' })
export class TelemetryStreamService {
  private readonly config = inject(API_CONFIG);
  private readonly deltas = new Subject<DeviceState[]>();
  private readonly alerts = new Subject<AlertIncident[]>();
  private readonly connected = new Subject<void>();
  private connection?: HubConnection;

  /** Estado de conexión, para que la UI muestre "en vivo / reconectando". */
  readonly status = signal<HubConnectionState>(HubConnectionState.Disconnected);

  /** Lote de deltas tal como llegan del hub (`devicesUpdated`). */
  readonly deltas$: Observable<DeviceState[]> = this.deltas.asObservable();

  /** Transiciones de incidentes tal como llegan del hub (`alertsRaised`). */
  readonly alerts$: Observable<AlertIncident[]> = this.alerts.asObservable();

  /**
   * Emite en cada (re)conexión exitosa. El store lo usa para re-sincronizar el snapshot
   * y así no quedar con huecos tras una caída (ADR-006).
   */
  readonly connected$: Observable<void> = this.connected.asObservable();

  /** Último viewport solicitado (null = firehose). Se re-aplica tras reconectar. */
  private viewport: string[] | null = null;

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

    this.connection.on('alertsRaised', (batch: AlertIncident[]) =>
      this.alerts.next(batch)
    );

    this.connection.onreconnecting(() => this.status.set(HubConnectionState.Reconnecting));
    this.connection.onreconnected(() => {
      this.status.set(HubConnectionState.Connected);
      void this.reapplyViewport(); // el servidor perdió el estado de grupos al reconectar
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

  /**
   * Modo viewport (AUD-008): el cliente solo recibe los deltas de `deviceIds`. `null` vuelve al
   * firehose. El servidor pierde los grupos al reconectar, por eso guardamos el último viewport.
   */
  async setViewport(deviceIds: string[] | null): Promise<void> {
    const wasFirehose = this.viewport === null;
    this.viewport = deviceIds;
    if (this.connection?.state !== HubConnectionState.Connected) return;
    if (deviceIds === null) {
      if (!wasFirehose) await this.connection.invoke('ClearViewport'); // ya en firehose: nada que limpiar
    } else {
      await this.connection.invoke('SyncViewport', deviceIds);
    }
  }

  private async reapplyViewport(): Promise<void> {
    if (this.connection?.state !== HubConnectionState.Connected) return;
    if (this.viewport === null) return; // firehose: nada que re-suscribir
    await this.connection.invoke('SyncViewport', this.viewport);
  }
}
