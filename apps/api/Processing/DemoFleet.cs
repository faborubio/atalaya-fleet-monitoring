using Atalaya.Api.Services;
using Atalaya.Contracts;

namespace Atalaya.Api.Processing;

/// <summary>
/// Flota sintética para la demo de portafolio (ADR-014, ver DEMO.md). Mantiene el estado de N
/// dispositivos y avanza un "tick": movimiento fluido (random-walk con heading coherente, tirando al
/// centro para mantener el cluster) + variación suave de métricas, e <b>inyecta anomalías a propósito</b>
/// cada N ticks para que se vean alertas abrirse y resolverse (temperatura/velocidad/combustible cruzan
/// los umbrales de <see cref="AlertRules"/> y luego se normalizan). Sin dependencias de hosting →
/// testeable en aislamiento (semilla fija). El <see cref="DemoTelemetryGenerator"/> lo envuelve.
/// </summary>
public sealed class DemoFleet
{
    private sealed class Device
    {
        public required string Id { get; init; }
        public double Lat, Lng, HeadingDeg, SpeedKmh, FuelPct, EngineTempC;
        public int AnomalyTicksLeft;
        public int AnomalyKind; // 0 = temperatura, 1 = velocidad, 2 = combustible
    }

    private readonly DemoOptions _o;
    private readonly Random _rng;
    private readonly Device[] _devices;
    private long _seq;
    private int _tick;

    public DemoFleet(DemoOptions options)
    {
        _o = options;
        _rng = options.Seed > 0 ? new Random(options.Seed) : new Random();
        _devices = Enumerable.Range(0, Math.Max(1, options.Devices))
            .Select(i => new Device
            {
                Id = $"demo-{i:D3}",
                Lat = options.Lat + (_rng.NextDouble() - 0.5) * options.SpreadDeg,
                Lng = options.Lng + (_rng.NextDouble() - 0.5) * options.SpreadDeg,
                HeadingDeg = _rng.NextDouble() * 360,
                SpeedKmh = 30 + _rng.NextDouble() * 50,    // 30-80 (zona sana)
                FuelPct = 40 + _rng.NextDouble() * 60,     // 40-100
                EngineTempC = 70 + _rng.NextDouble() * 12, // 70-82
            })
            .ToArray();
    }

    public int Count => _devices.Length;

    /// <summary>Avanza un tick y devuelve un evento por dispositivo.</summary>
    public IReadOnlyList<TelemetryEvent> Step()
    {
        _tick++;

        // Cada N ticks, mete a un dispositivo sano en anomalía durante unos ticks (luego recupera).
        if (_o.AlertEveryNTicks > 0 && _tick % _o.AlertEveryNTicks == 0)
        {
            var d = _devices[_rng.Next(_devices.Length)];
            if (d.AnomalyTicksLeft == 0)
            {
                d.AnomalyKind = _rng.Next(3);
                d.AnomalyTicksLeft = 3 + _rng.Next(3); // 3-5 ticks en alerta
            }
        }

        var now = DateTimeOffset.UtcNow;
        var batch = new List<TelemetryEvent>(_devices.Length);
        foreach (var d in _devices)
        {
            Advance(d);
            batch.Add(new TelemetryEvent(
                EventId: Guid.NewGuid().ToString("N"),
                DeviceId: d.Id,
                Ts: now,
                Seq: ++_seq,
                Lat: Math.Round(d.Lat, 5),
                Lng: Math.Round(d.Lng, 5),
                SpeedKmh: Math.Round(d.SpeedKmh, 1),
                HeadingDeg: Math.Round(d.HeadingDeg, 1),
                FuelPct: Math.Round(d.FuelPct, 1),
                EngineTempC: Math.Round(d.EngineTempC, 1)));
        }
        return batch;
    }

    private void Advance(Device d)
    {
        // Heading deriva suave; la posición avanza en su dirección (movimiento coherente, no saltos).
        d.HeadingDeg = (d.HeadingDeg + (_rng.NextDouble() - 0.5) * 20 + 360) % 360;
        var rad = d.HeadingDeg * Math.PI / 180;
        var stepDeg = d.SpeedKmh * 1e-5; // paso proporcional a la velocidad (visible al zoom de ciudad)
        d.Lat += Math.Cos(rad) * stepDeg;
        d.Lng += Math.Sin(rad) * stepDeg;
        // Tira al centro para que la flota no se disperse fuera del viewport.
        d.Lat += (_o.Lat - d.Lat) * 0.02;
        d.Lng += (_o.Lng - d.Lng) * 0.02;

        if (d.AnomalyTicksLeft > 0)
        {
            d.AnomalyTicksLeft--;
            switch (d.AnomalyKind)
            {
                case 0: d.EngineTempC = 112 + _rng.NextDouble() * 4; break; // crítico (>= 110)
                case 1: d.SpeedKmh = 125 + _rng.NextDouble() * 12; break;   // aviso/crítico (>= 120)
                default: d.FuelPct = 6 + _rng.NextDouble() * 3; break;       // crítico (<= 10)
            }
        }
        else
        {
            // Deriva normal hacia rangos sanos (también recupera tras una anomalía → la alerta se resuelve).
            d.SpeedKmh = Clamp(d.SpeedKmh + (_rng.NextDouble() - 0.5) * 10, 0, 90);
            d.EngineTempC = Clamp(d.EngineTempC + (_rng.NextDouble() - 0.55) * 4, 68, 88);
            d.FuelPct = Clamp(d.FuelPct - _rng.NextDouble() * 0.3, 30, 100);
        }
    }

    private static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));
}
