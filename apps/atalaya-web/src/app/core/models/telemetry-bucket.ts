/**
 * Punto agregado del histórico (downsampling, espejo de Atalaya.Contracts.TelemetryBucket).
 * Las métricas son el promedio del intervalo; se nombran igual que en TelemetryEvent para que el
 * gráfico y el selector de métrico se reusen sin cambios.
 */
export interface TelemetryBucket {
  ts: string;
  count: number;
  speedKmh: number;
  fuelPct: number;
  engineTempC: number;
}
