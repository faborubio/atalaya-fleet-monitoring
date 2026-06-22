import { readConfig } from './lib/config';
import { Fleet } from './lib/fleet';
import { TelemetryEvent } from './lib/types';

/**
 * Simulador de telemetría de Atalaya (SAD §1.3).
 * Generador de carga configurable: produce eventos de flota a un ritmo dado y,
 * si se indica un endpoint de ingesta, los envía por HTTP. Sin endpoint corre
 * "en seco" mostrando solo métricas — útil ya en Fase 0, antes de que exista
 * el backend.
 */
async function main(): Promise<void> {
  const cfg = readConfig(process.argv.slice(2));
  const fleet = new Fleet(cfg.devices);

  const TICKS_PER_SEC = 10;
  const perTick = Math.max(1, Math.round(cfg.rate / TICKS_PER_SEC));
  const headers: Record<string, string> = { 'content-type': 'application/json' };
  if (cfg.ingestToken) headers['x-ingest-token'] = cfg.ingestToken;
  const mode = cfg.ingestUrl ? `POST → ${cfg.ingestUrl}` : 'dry-run (solo consola)';

  console.log(
    `[simulador] ${cfg.rate} ev/s · ${cfg.devices} dispositivos · ` +
      `${cfg.durationSec ? cfg.durationSec + 's' : '∞'} · ${mode}`
  );

  let cursor = 0;
  let sent = 0;
  let failed = 0;
  let sinceReport = 0;
  const started = Date.now();

  const tick = setInterval(() => {
    const batch: TelemetryEvent[] = [];
    for (let i = 0; i < perTick; i++) {
      batch.push(fleet.next(cursor++));
    }

    if (cfg.ingestUrl) {
      void ship(cfg.ingestUrl, headers, batch).then((ok) => {
        if (ok) sent += batch.length;
        else failed += batch.length;
      });
    } else {
      sent += batch.length;
    }
    sinceReport += batch.length;
  }, 1000 / TICKS_PER_SEC);

  const report = setInterval(() => {
    const sample = sinceReport;
    sinceReport = 0;
    console.log(
      `[simulador] ~${sample} ev/s · enviados=${sent} fallidos=${failed}`
    );
  }, 1000);

  if (cfg.durationSec > 0) {
    setTimeout(() => {
      clearInterval(tick);
      clearInterval(report);
      const secs = ((Date.now() - started) / 1000).toFixed(1);
      console.log(
        `[simulador] fin tras ${secs}s · total enviados=${sent} fallidos=${failed}`
      );
      process.exit(0);
    }, cfg.durationSec * 1000);
  }

  const stop = () => {
    clearInterval(tick);
    clearInterval(report);
    console.log('\n[simulador] detenido.');
    process.exit(0);
  };
  process.on('SIGINT', stop);
  process.on('SIGTERM', stop);
}

async function ship(
  url: string,
  headers: Record<string, string>,
  batch: TelemetryEvent[]
): Promise<boolean> {
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers,
      body: JSON.stringify(batch),
    });
    return res.ok;
  } catch {
    return false;
  }
}

void main();
