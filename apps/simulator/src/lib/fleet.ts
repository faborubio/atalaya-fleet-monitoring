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
  routeIdx: number; // arteria asignada
  segIdx: number; // índice del vértice "desde" en la polilínea
  segT: number; // fracción [0,1) recorrida del segmento actual
  dir: number; // sentido sobre la polilínea (+1 / -1)
}

// Arterias reales de Santiago como polilíneas [lat, lng] (paridad con DemoFleet.cs del backend).
// Geometría real de OpenStreetMap (Overpass), promediada a una línea central por corredor (datos
// © OpenStreetMap, ODbL). Los vehículos las recorren ida y vuelta → la traza va sobre la calle real.
const ROUTES: number[][][] = [
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

/**
 * Flota simulada: cada dispositivo recorre una arteria real de Santiago (polilíneas de `ROUTES`),
 * interpolando sobre la calle y rebotando en los extremos, para que la traza vaya por la vía y no
 * cruce edificios (paridad con el generador de demo del backend).
 */
export class Fleet {
  private readonly devices: DeviceState[];

  constructor(count: number) {
    this.devices = Array.from({ length: count }, (_, i) => {
      const routeIdx = i % ROUTES.length;
      const route = ROUTES[routeIdx];
      const d: DeviceState = {
        deviceId: `dev-${String(i + 1).padStart(5, '0')}`,
        seq: 0,
        lat: 0,
        lng: 0,
        headingDeg: 0,
        speedKmh: rand(0, 80),
        fuelPct: rand(20, 100),
        engineTempC: rand(70, 95),
        routeIdx,
        segIdx: Math.floor(Math.random() * (route.length - 1)),
        segT: Math.random(),
        dir: Math.random() < 0.5 ? 1 : -1,
      };
      projectOntoRoute(d);
      return d;
    });
  }

  /** Avanza un dispositivo (round-robin por índice) y devuelve su evento. */
  next(index: number): TelemetryEvent {
    const d = this.devices[index % this.devices.length];

    // Recorre la polilínea de su arteria una distancia proporcional a la velocidad, cruzando
    // vértices y rebotando en los extremos; la posición sale interpolada sobre la vía.
    d.speedKmh = clamp(d.speedKmh + rand(-8, 8), 0, 120);
    const route = ROUTES[d.routeIdx];
    let budget = Math.max(d.speedKmh, 5) * 1e-5; // grados a recorrer este tick
    while (budget > 0) {
      let to = d.segIdx + d.dir;
      if (to < 0 || to >= route.length) {
        d.dir = -d.dir; // rebote en el extremo
        to = d.segIdx + d.dir;
        if (to < 0 || to >= route.length) break;
      }
      const segLen = dist(route[d.segIdx], route[to]);
      if (segLen < 1e-9) {
        d.segIdx = to;
        d.segT = 0;
        continue;
      }
      const remainOnSeg = segLen * (1 - d.segT);
      if (budget < remainOnSeg) {
        d.segT += budget / segLen;
        budget = 0;
      } else {
        budget -= remainOnSeg;
        d.segIdx = to;
        d.segT = 0;
      }
    }
    projectOntoRoute(d);
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

/** Fija lat/lng (interpolados en el segmento) y el rumbo en dirección de marcha. */
function projectOntoRoute(d: DeviceState): void {
  const route = ROUTES[d.routeIdx];
  let to = d.segIdx + d.dir;
  if (to < 0 || to >= route.length) to = d.segIdx - d.dir;
  const a = route[d.segIdx];
  const b = route[to];
  d.lat = a[0] + (b[0] - a[0]) * d.segT;
  d.lng = a[1] + (b[1] - a[1]) * d.segT;
  d.headingDeg = (Math.atan2(b[1] - a[1], b[0] - a[0]) * 180) / Math.PI; // 0=N, 90=E
  if (d.headingDeg < 0) d.headingDeg += 360;
}

/** Distancia planar (grados) entre dos puntos [lat, lng]. */
function dist(a: number[], b: number[]): number {
  const dLat = b[0] - a[0];
  const dLng = b[1] - a[1];
  return Math.sqrt(dLat * dLat + dLng * dLng);
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
