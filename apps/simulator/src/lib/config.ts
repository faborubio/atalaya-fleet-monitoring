import { SimulatorConfig } from './types';

/**
 * Lee la configuración desde flags de CLI (con fallback a variables de entorno).
 * Ejemplos:
 *   nx serve simulator -- --rate 1000 --devices 200 --duration 30
 *   INGEST_URL=http://localhost:3000/ingest nx serve simulator -- --rate 5000
 */
export function readConfig(argv: string[]): SimulatorConfig {
  const flags = parseFlags(argv);

  const num = (key: string, env: string, fallback: number): number => {
    const raw = flags[key] ?? process.env[env];
    const n = raw === undefined ? fallback : Number(raw);
    return Number.isFinite(n) && n >= 0 ? n : fallback;
  };

  return {
    rate: num('rate', 'SIM_RATE', 1000),
    devices: Math.max(1, num('devices', 'SIM_DEVICES', 200)),
    durationSec: num('duration', 'SIM_DURATION', 0),
    ingestUrl: (flags['url'] ?? process.env['INGEST_URL'] ?? '').trim(),
    ingestToken: (flags['token'] ?? process.env['INGEST_TOKEN'] ?? '').trim(),
  };
}

function parseFlags(argv: string[]): Record<string, string> {
  const out: Record<string, string> = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (!arg.startsWith('--')) continue;
    const key = arg.slice(2);
    const next = argv[i + 1];
    if (next !== undefined && !next.startsWith('--')) {
      out[key] = next;
      i++;
    } else {
      out[key] = 'true';
    }
  }
  return out;
}
