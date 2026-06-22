// Prueba de carga de ingesta de Atalaya (SAD §10: la carga es parte del Definition of Done).
// Cada iteración envía un lote de BATCH eventos a /ingest; el ritmo de llegada (iter/seg)
// por BATCH da los eventos/seg. Objetivo: sostener ~5.000 ev/seg y medir pérdida y latencia.
//
// Ejecutar (vía Docker, sin instalar k6; la API corre en el host):
//   docker run --rm -i -e INGEST_URL=http://host.docker.internal:3000/ingest \
//     -e INGEST_TOKEN=dev-ingest-token grafana/k6 run - < infra/load/ingest.js
import http from 'k6/http';
import { check } from 'k6';

const URL = __ENV.INGEST_URL || 'http://host.docker.internal:3000/ingest';
const TOKEN = __ENV.INGEST_TOKEN || 'dev-ingest-token';
const BATCH = Number(__ENV.BATCH || 100); // eventos por request
const DEVICES = Number(__ENV.DEVICES || 500);

export const options = {
  scenarios: {
    ingest: {
      executor: 'ramping-arrival-rate',
      timeUnit: '1s',
      startRate: 5, // 5 req/s * BATCH = ev/s inicial
      preAllocatedVUs: 50,
      maxVUs: 300,
      stages: [
        { target: 10, duration: '15s' }, // ~1.000 ev/s
        { target: 50, duration: '20s' }, // ~5.000 ev/s
        { target: 50, duration: '30s' }, // sostener 5.000 ev/s
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'], // < 1% de error
    http_req_duration: ['p(95)<500'], // p95 de respuesta de /ingest < 500 ms
  },
};

export default function () {
  const now = new Date().toISOString();
  const batch = new Array(BATCH);
  for (let i = 0; i < BATCH; i++) {
    batch[i] = {
      eventId: `${__VU}-${__ITER}-${i}-${Date.now()}`,
      deviceId: `dev-${(Math.random() * DEVICES) | 0}`,
      ts: now,
      seq: __ITER,
      lat: 19.4 + Math.random() * 0.3,
      lng: -99.2 + Math.random() * 0.3,
      speedKmh: Math.random() * 120,
      headingDeg: Math.random() * 360,
      fuelPct: Math.random() * 100,
      engineTempC: 70 + Math.random() * 40,
    };
  }
  const res = http.post(URL, JSON.stringify(batch), {
    headers: { 'Content-Type': 'application/json', 'X-Ingest-Token': TOKEN },
  });
  check(res, { 'status 202': (r) => r.status === 202 });
}
