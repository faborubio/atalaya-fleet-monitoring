/** Read model del camino caliente (espejo de Atalaya.Contracts.DeviceState). */
export interface DeviceState {
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
