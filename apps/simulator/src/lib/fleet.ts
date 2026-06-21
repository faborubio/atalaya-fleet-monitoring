import { randomUUID } from 'node:crypto';
import { TelemetryEvent } from './types';

interface DeviceState {
  deviceId: string;
  seq: number;
  lat: number;
  lng: number;
  headingDeg: number;
  speedKmh: number;
  fuelPct: number;
  engineTempC: number;
}

/**
 * Flota simulada: cada dispositivo mantiene una posición que evoluciona de forma
 * coherente (no saltos aleatorios) para que el dashboard muestre movimiento real.
 */
export class Fleet {
  private readonly devices: DeviceState[];

  constructor(count: number) {
    this.devices = Array.from({ length: count }, (_, i) => ({
      deviceId: `dev-${String(i + 1).padStart(5, '0')}`,
      seq: 0,
      // Área aproximada de Ciudad de México como punto de partida.
      lat: 19.4326 + rand(-0.15, 0.15),
      lng: -99.1332 + rand(-0.15, 0.15),
      headingDeg: rand(0, 360),
      speedKmh: rand(0, 80),
      fuelPct: rand(20, 100),
      engineTempC: rand(70, 95),
    }));
  }

  /** Avanza un dispositivo (round-robin por índice) y devuelve su evento. */
  next(index: number): TelemetryEvent {
    const d = this.devices[index % this.devices.length];

    // Movimiento coherente: pequeño cambio de rumbo y desplazamiento por velocidad.
    d.headingDeg = (d.headingDeg + rand(-15, 15) + 360) % 360;
    d.speedKmh = clamp(d.speedKmh + rand(-8, 8), 0, 120);
    const distDeg = (d.speedKmh / 3600) * 0.01; // escala aprox. por tick
    const rad = (d.headingDeg * Math.PI) / 180;
    d.lat = clamp(d.lat + Math.cos(rad) * distDeg, -85, 85);
    d.lng = clamp(d.lng + Math.sin(rad) * distDeg, -180, 180);
    d.fuelPct = clamp(d.fuelPct - rand(0, 0.05), 0, 100);
    d.engineTempC = clamp(d.engineTempC + rand(-1, 1), 60, 120);
    d.seq += 1;

    return {
      eventId: randomUUID(),
      deviceId: d.deviceId,
      ts: new Date().toISOString(),
      seq: d.seq,
      lat: round(d.lat, 6),
      lng: round(d.lng, 6),
      speedKmh: round(d.speedKmh, 1),
      headingDeg: round(d.headingDeg, 1),
      fuelPct: round(d.fuelPct, 1),
      engineTempC: round(d.engineTempC, 1),
    };
  }
}

function rand(min: number, max: number): number {
  return Math.random() * (max - min) + min;
}
function clamp(v: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, v));
}
function round(v: number, decimals: number): number {
  const f = 10 ** decimals;
  return Math.round(v * f) / f;
}
