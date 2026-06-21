import { Fleet } from './fleet';

describe('Fleet', () => {
  it('genera eventos con eventId único y seq creciente por dispositivo', () => {
    const fleet = new Fleet(1);
    const a = fleet.next(0);
    const b = fleet.next(0);

    expect(a.deviceId).toBe(b.deviceId);
    expect(a.eventId).not.toBe(b.eventId);
    expect(b.seq).toBe(a.seq + 1);
  });

  it('mantiene coordenadas dentro de rango válido', () => {
    const fleet = new Fleet(50);
    for (let i = 0; i < 500; i++) {
      const e = fleet.next(i);
      expect(e.lat).toBeGreaterThanOrEqual(-85);
      expect(e.lat).toBeLessThanOrEqual(85);
      expect(e.lng).toBeGreaterThanOrEqual(-180);
      expect(e.lng).toBeLessThanOrEqual(180);
      expect(e.speedKmh).toBeGreaterThanOrEqual(0);
      expect(e.fuelPct).toBeGreaterThanOrEqual(0);
      expect(e.fuelPct).toBeLessThanOrEqual(100);
    }
  });
});
