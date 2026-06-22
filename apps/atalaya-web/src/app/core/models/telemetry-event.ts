/** Evento de telemetría crudo del camino frío (espejo de Atalaya.Contracts.TelemetryEvent). */
export interface TelemetryEvent {
  eventId: string;
  deviceId: string;
  ts: string;
  seq: number;
  lat: number;
  lng: number;
  speedKmh: number;
  headingDeg: number;
  fuelPct: number;
  engineTempC: number;
}
