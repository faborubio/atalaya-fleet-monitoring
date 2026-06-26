using Atalaya.Api.Services;
using Atalaya.Contracts;

namespace Atalaya.Api.Processing;

/// <summary>
/// Flota sintética para la demo de portafolio (ADR-014, ver DEMO.md). Mantiene el estado de N
/// dispositivos y avanza un "tick": cada vehículo <b>recorre una arteria real de Santiago</b>
/// (polilíneas en <see cref="Routes"/> con geometría real de OpenStreetMap: Alameda, Providencia–
/// Apoquindo, Vicuña Mackenna, Gran Avenida, Irarrázaval), interpolando sobre la calle y rebotando en
/// los extremos → los puntos van por la vía, no cruzan edificios. La velocidad de avance es <b>física</b>
/// (km/h reales, escalables con <c>Demo:SpeedFactor</c>) y respeta <b>semáforos</b> (paradas en rojo,
/// stop-and-go). Además hay variación suave de métricas e <b>inyecta anomalías a propósito</b> cada N
/// ticks para que se vean alertas abrirse y resolverse
/// (temperatura/velocidad/combustible cruzan los umbrales de <see cref="AlertRules"/> y luego se
/// normalizan). Sin dependencias de hosting → testeable en aislamiento (semilla fija). El
/// <see cref="DemoTelemetryGenerator"/> lo envuelve.
/// </summary>
public sealed class DemoFleet
{
    // Arterias reales de Santiago como polilíneas [lat, lng]. Geometría descargada de OpenStreetMap
    // (Overpass) y promediada a una línea central por corredor (datos © OpenStreetMap, ODbL). Los
    // vehículos las recorren ida y vuelta, así que la traza va sobre la calle real (no cruza edificios).
    private static readonly double[][][] Routes =
    [
        // Alameda / Av. Libertador B. O'Higgins (E-O central).
        [[-33.45856, -70.713], [-33.45862, -70.7112], [-33.45829, -70.7094], [-33.45783, -70.7076], [-33.45734, -70.7058], [-33.45687, -70.704], [-33.45652, -70.7022], [-33.45617, -70.7004], [-33.45563, -70.6986], [-33.45524, -70.6968], [-33.45498, -70.695], [-33.45428, -70.6932], [-33.45398, -70.6914], [-33.45356, -70.6896], [-33.45328, -70.6878], [-33.45279, -70.686], [-33.45227, -70.6842], [-33.45193, -70.6824], [-33.45135, -70.6806], [-33.45083, -70.6788], [-33.4505, -70.677], [-33.44988, -70.6752], [-33.44937, -70.6734], [-33.44864, -70.6716], [-33.44851, -70.6698], [-33.44789, -70.668], [-33.44742, -70.6662], [-33.44709, -70.6644], [-33.44662, -70.6626], [-33.44626, -70.6608], [-33.44577, -70.659], [-33.44542, -70.6572], [-33.445, -70.6554], [-33.44455, -70.6536], [-33.4441, -70.6518], [-33.44363, -70.65], [-33.44322, -70.6482], [-33.44287, -70.6464], [-33.44241, -70.6446], [-33.44172, -70.6428], [-33.44058, -70.641], [-33.43954, -70.6392], [-33.43801, -70.6374], [-33.43732, -70.6356]],
        // Av. Providencia → Av. Apoquindo (E-O oriente).
        [[-33.43718, -70.6356], [-33.43709, -70.6338], [-33.43638, -70.632], [-33.43582, -70.6302], [-33.43484, -70.6284], [-33.43374, -70.6266], [-33.43218, -70.6248], [-33.43061, -70.623], [-33.42934, -70.6212], [-33.42854, -70.6194], [-33.42762, -70.6176], [-33.42679, -70.6158], [-33.42493, -70.614], [-33.42342, -70.6122], [-33.4222, -70.6104], [-33.42098, -70.6086], [-33.42007, -70.6068], [-33.41951, -70.605], [-33.41882, -70.6032], [-33.41809, -70.6014], [-33.41764, -70.5996], [-33.41705, -70.5978], [-33.41662, -70.596], [-33.41627, -70.5942], [-33.41587, -70.5924], [-33.41546, -70.5906], [-33.4152, -70.5888], [-33.41485, -70.587], [-33.41451, -70.5852], [-33.4143, -70.5834], [-33.41297, -70.5816], [-33.41242, -70.5798], [-33.41188, -70.578], [-33.41144, -70.5762], [-33.41078, -70.5744], [-33.41016, -70.5726], [-33.40972, -70.5708], [-33.40916, -70.569], [-33.40874, -70.5672], [-33.40835, -70.5654], [-33.40788, -70.5636], [-33.4075, -70.5618], [-33.40755, -70.56], [-33.40764, -70.5582], [-33.40793, -70.5564], [-33.40822, -70.5546], [-33.40842, -70.5528], [-33.40836, -70.551], [-33.40822, -70.5492], [-33.40808, -70.5474], [-33.40794, -70.5456]],
        // Av. Vicuña Mackenna (N-S).
        [[-33.4368, -70.63527], [-33.4386, -70.63493], [-33.4404, -70.63443], [-33.4422, -70.63385], [-33.444, -70.63337], [-33.4458, -70.6328], [-33.4476, -70.63219], [-33.4494, -70.63159], [-33.4512, -70.63106], [-33.453, -70.63061], [-33.4548, -70.63001], [-33.4566, -70.62961], [-33.4584, -70.62907], [-33.4602, -70.62843], [-33.462, -70.62796], [-33.4638, -70.62726], [-33.4656, -70.62711], [-33.4674, -70.62642], [-33.4692, -70.62549], [-33.471, -70.62403], [-33.4728, -70.62349], [-33.4746, -70.62301], [-33.4764, -70.62256], [-33.4782, -70.62214], [-33.48, -70.62168], [-33.4818, -70.62125], [-33.4836, -70.62053], [-33.4854, -70.61971], [-33.4872, -70.61875], [-33.489, -70.61807], [-33.4908, -70.61766], [-33.4926, -70.61716], [-33.4944, -70.61686], [-33.4962, -70.61653], [-33.498, -70.616], [-33.4998, -70.61557], [-33.5016, -70.61532], [-33.5034, -70.61494], [-33.5052, -70.61451], [-33.507, -70.61374], [-33.5088, -70.61198]],
        // Gran Avenida José Miguel Carrera (N-S sur).
        [[-33.4764, -70.64821], [-33.4782, -70.64824], [-33.48, -70.64825], [-33.4818, -70.64889], [-33.4836, -70.64952], [-33.4854, -70.64997], [-33.4872, -70.65054], [-33.489, -70.65092], [-33.4908, -70.65136], [-33.4926, -70.65182], [-33.4944, -70.65241], [-33.4962, -70.65289], [-33.498, -70.65324], [-33.4998, -70.65386], [-33.5016, -70.65436], [-33.5034, -70.65493], [-33.5052, -70.65537], [-33.507, -70.65599], [-33.5088, -70.65651], [-33.5106, -70.65696], [-33.5124, -70.65745], [-33.5142, -70.658], [-33.516, -70.6585], [-33.5178, -70.65902], [-33.5196, -70.65949], [-33.5214, -70.65994], [-33.5232, -70.66053], [-33.525, -70.66093], [-33.5268, -70.6615], [-33.5286, -70.66204], [-33.5304, -70.66243], [-33.5322, -70.66292], [-33.534, -70.66345], [-33.5358, -70.66401], [-33.5376, -70.66447], [-33.5394, -70.66484], [-33.5412, -70.66547], [-33.543, -70.66603], [-33.5448, -70.66798], [-33.5466, -70.6691], [-33.5484, -70.67077], [-33.5502, -70.67175], [-33.552, -70.67332], [-33.5538, -70.67472], [-33.5556, -70.67604], [-33.5574, -70.67755], [-33.5592, -70.67877], [-33.561, -70.68052], [-33.5628, -70.68172], [-33.5646, -70.68312], [-33.5664, -70.6846], [-33.5682, -70.68617], [-33.57, -70.68737], [-33.5718, -70.6886], [-33.5736, -70.69019], [-33.5754, -70.69144], [-33.5772, -70.69243], [-33.579, -70.69419], [-33.5808, -70.69533], [-33.5826, -70.6968], [-33.5844, -70.69856], [-33.5862, -70.6991]],
        // Av. Irarrázaval (E-O Ñuñoa).
        [[-33.45202, -70.6302], [-33.45245, -70.6284], [-33.45235, -70.6266], [-33.45243, -70.6248], [-33.45346, -70.623], [-33.45357, -70.6212], [-33.45326, -70.6194], [-33.45303, -70.6176], [-33.45302, -70.6158], [-33.45314, -70.614], [-33.4533, -70.6122], [-33.45349, -70.6104], [-33.45372, -70.6086], [-33.45393, -70.6068], [-33.45416, -70.605], [-33.45436, -70.6032], [-33.45448, -70.6014], [-33.45464, -70.5996], [-33.45491, -70.5978], [-33.45511, -70.596], [-33.45527, -70.5942], [-33.45532, -70.5924], [-33.45545, -70.5906], [-33.45551, -70.5888], [-33.45552, -70.587], [-33.45542, -70.5852], [-33.45489, -70.5834], [-33.45464, -70.5816], [-33.45453, -70.5798], [-33.45441, -70.578], [-33.4541, -70.5762], [-33.45342, -70.5744], [-33.45345, -70.5726], [-33.45352, -70.5708]],
    ];

    private sealed class Device
    {
        public required string Id { get; init; }
        public double Lat, Lng, HeadingDeg, SpeedKmh, FuelPct, EngineTempC;
        public int RouteIdx;  // arteria asignada
        public int SegIdx;    // índice del vértice "desde" en la polilínea
        public double SegT;   // fracción [0,1) recorrida del segmento actual
        public int Dir = 1;   // sentido sobre la polilínea (+1 / -1)
        public double DistToLightDeg; // distancia restante hasta el próximo cruce con semáforo
        public int StopTicksLeft;     // ticks que quedan detenido en rojo (0 = en marcha)
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
            .Select(i =>
            {
                var routeIdx = i % Routes.Length;
                var route = Routes[routeIdx];
                var d = new Device
                {
                    Id = $"demo-{i:D3}",
                    RouteIdx = routeIdx,
                    SegIdx = _rng.Next(route.Length - 1), // arranca repartido a lo largo de la vía
                    SegT = _rng.NextDouble(),
                    Dir = _rng.Next(2) == 0 ? 1 : -1,
                    DistToLightDeg = NextLightGap(),
                    SpeedKmh = 30 + _rng.NextDouble() * 50,    // 30-80 (zona sana)
                    FuelPct = 40 + _rng.NextDouble() * 60,     // 40-100
                    EngineTempC = 70 + _rng.NextDouble() * 12, // 70-82
                };
                ProjectOntoRoute(d); // fija Lat/Lng/Heading sobre la polilínea
                return d;
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
        // 1) Métricas y anomalías (independientes del movimiento).
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

        // 2) Semáforo en rojo: detenido, no avanza y reporta velocidad 0 (stop-and-go).
        if (d.StopTicksLeft > 0)
        {
            d.StopTicksLeft--;
            d.SpeedKmh = 0;
            return;
        }

        // 3) Avanza por la arteria una distancia físicamente realista: km/tick = speed·(intervalMs/3.6e6),
        //    a grados ≈ /111 (1° lat ≈ 111 km). SpeedFactor permite acelerar la demo. Cruza vértices y
        //    rebota en los extremos; la posición sale interpolada sobre la vía (no cruza edificios).
        var route = Routes[d.RouteIdx];
        var stepDeg = d.SpeedKmh * (_o.IntervalMs / 3_600_000.0) / 111.0 * Math.Max(0.1, _o.SpeedFactor);
        var budget = stepDeg;
        while (budget > 0)
        {
            var to = d.SegIdx + d.Dir;
            if (to < 0 || to >= route.Length)
            {
                d.Dir = -d.Dir; // rebote en el extremo de la arteria
                to = d.SegIdx + d.Dir;
                if (to < 0 || to >= route.Length) break; // ruta degenerada (no debería)
            }
            var segLen = Dist(route[d.SegIdx], route[to]);
            if (segLen < 1e-9) { d.SegIdx = to; d.SegT = 0; continue; }

            var remainOnSeg = segLen * (1 - d.SegT);
            if (budget < remainOnSeg)
            {
                d.SegT += budget / segLen;
                budget = 0;
            }
            else
            {
                budget -= remainOnSeg;
                d.SegIdx = to;
                d.SegT = 0;
            }
        }
        ProjectOntoRoute(d);

        // 4) Semáforos: al recorrer la distancia entre cruces, a veces "pilla rojo" y se detiene.
        d.DistToLightDeg -= stepDeg;
        if (d.DistToLightDeg <= 0)
        {
            d.DistToLightDeg = NextLightGap();
            if (_rng.NextDouble() < 0.5) d.StopTicksLeft = RedTicks();
        }
    }

    /// <summary>Distancia (grados) hasta el próximo cruce con semáforo: ~250–450 m.</summary>
    private double NextLightGap() => 0.0025 + _rng.NextDouble() * 0.0020;

    /// <summary>Duración de un rojo en ticks (15–40 s reales, según <c>IntervalMs</c>).</summary>
    private int RedTicks()
    {
        var redSeconds = 15 + _rng.NextDouble() * 25;
        return Math.Max(1, (int)Math.Round(redSeconds * 1000 / Math.Max(1, _o.IntervalMs)));
    }

    /// <summary>Fija Lat/Lng (interpolados sobre el segmento actual) y el rumbo en dirección de marcha.</summary>
    private static void ProjectOntoRoute(Device d)
    {
        var route = Routes[d.RouteIdx];
        var to = d.SegIdx + d.Dir;
        if (to < 0 || to >= route.Length) to = d.SegIdx - d.Dir; // en el borde, mira al segmento válido
        var a = route[d.SegIdx];
        var b = route[to];
        d.Lat = a[0] + (b[0] - a[0]) * d.SegT;
        d.Lng = a[1] + (b[1] - a[1]) * d.SegT;
        d.HeadingDeg = Bearing(a, b);
    }

    /// <summary>Distancia planar (grados) entre dos puntos [lat, lng]; basta a escala de ciudad.</summary>
    private static double Dist(double[] a, double[] b)
    {
        var dLat = b[0] - a[0];
        var dLng = b[1] - a[1];
        return Math.Sqrt(dLat * dLat + dLng * dLng);
    }

    /// <summary>Rumbo a→b en grados (0=N, 90=E), convención de <see cref="TelemetryEvent"/>.</summary>
    private static double Bearing(double[] a, double[] b)
    {
        var deg = Math.Atan2(b[1] - a[1], b[0] - a[0]) * 180 / Math.PI; // atan2(Δlng=E, Δlat=N)
        return (deg + 360) % 360;
    }

    private static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));
}
