/**
 * Modelo de evento de telemetría que produce el simulador.
 * Refleja el modelo de datos del SAD §6 e incluye las claves que el pipeline
 * necesita para garantizar entrega exactly-once a nivel de efecto (ADR-006):
 *  - `eventId`: clave de deduplicación (idempotencia en worker + constraint SQL).
 *  - `seq`: secuencia por dispositivo, para detectar/rellenar gaps en reconexión.
 */
export interface TelemetryEvent {
  eventId: string;
  deviceId: string;
  ts: string; // ISO-8601
  seq: number;
  lat: number;
  lng: number;
  speedKmh: number;
  headingDeg: number;
  fuelPct: number;
  engineTempC: number;
}

export interface SimulatorConfig {
  /** Eventos por segundo (en total, repartidos entre la flota). */
  rate: number;
  /** Número de dispositivos simulados. */
  devices: number;
  /** Duración en segundos. 0 = indefinido. */
  durationSec: number;
  /** Endpoint de ingesta. Si está vacío, corre en seco (solo métricas en consola). */
  ingestUrl: string;
}
