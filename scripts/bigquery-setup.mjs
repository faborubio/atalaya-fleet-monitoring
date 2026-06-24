// Crea el dataset y la EXTERNAL TABLE de BigQuery sobre el data lake en Cloud Storage
// (G4, ADR-013 / AUD-024). Es el equivalente GCP de Athena sobre S3: BigQuery consulta el lake
// directamente, sin copiar datos a una tabla nativa.
//
// El lake (escrito por GcsRawEventArchive) vive en gs://<bucket>/raw/yyyy/MM/dd/{sha256}.json,
// y cada objeto es NDJSON (un TelemetryEvent por línea, AUD-024) → BigQuery lo lee como filas con
// sourceFormat NEWLINE_DELIMITED_JSON. La external table apunta a gs://<bucket>/raw/*.json.
//
// Nota de particionado: el layout yyyy/MM/dd NO es hive-style (key=value), así que NO hay poda de
// particiones por carpeta; las consultas filtran por la columna `ts` (TIMESTAMP). A escala dev /
// free-tier el escaneo completo es trivial; migrar a un layout hive (dt=YYYY-MM-DD) es trabajo
// futuro si el volumen lo exige (tocaría RawEventKey, compartido con el S3 heredado).
//
// Requisitos:
//   - npm install  (trae @google-cloud/bigquery como devDependency)
//   - Una credencial con permiso para CREAR dataset + external table (roles/bigquery.dataEditor o
//     superior). Distinta de la SA de runtime de la API (esa solo necesita dataViewer + jobUser).
//     Vías de credencial:
//       a) ruta a una service account key como 4º argumento (recomendado, sin gcloud)
//       b) gcloud auth application-default login   (si tienes gcloud)
//       c) $env:GOOGLE_APPLICATION_CREDENTIALS = "ruta\\serviceAccountKey.json"
//
// Uso:
//   node scripts/bigquery-setup.mjs <projectId> <bucket> [datasetId] [ruta\a\serviceAccountKey.json]
//   p.ej. node scripts/bigquery-setup.mjs fabian-portafolio atalaya-datalake atalaya_analytics
//
// ⚠️ La service account key es un SECRETO: guárdala FUERA del repo, no la subas a git.

import { BigQuery } from '@google-cloud/bigquery';

const [projectId, bucket, datasetId = 'atalaya_analytics', keyPath] = process.argv.slice(2);

if (!projectId || !bucket) {
  console.error(
    'Uso: node scripts/bigquery-setup.mjs <projectId> <bucket> [datasetId] [keyPath.json]',
  );
  process.exit(1);
}

const tableId = 'telemetry_raw';
// La external table EXIGE que el dataset esté en la MISMA ubicación que el bucket del lake.
// Default US (multi-región); si el bucket está en una región concreta, fíjala con $env:BQ_LOCATION.
const location = process.env.BQ_LOCATION || 'US';

// Esquema explícito (camelCase: el backend serializa con JsonSerializerDefaults.Web, ver TelemetryEvent).
const schema = [
  { name: 'eventId', type: 'STRING' },
  { name: 'deviceId', type: 'STRING' },
  { name: 'ts', type: 'TIMESTAMP' },
  { name: 'seq', type: 'INTEGER' },
  { name: 'lat', type: 'FLOAT' },
  { name: 'lng', type: 'FLOAT' },
  { name: 'speedKmh', type: 'FLOAT' },
  { name: 'headingDeg', type: 'FLOAT' },
  { name: 'fuelPct', type: 'FLOAT' },
  { name: 'engineTempC', type: 'FLOAT' },
];

const bigquery = new BigQuery(keyPath ? { projectId, keyFilename: keyPath } : { projectId });

// 1) Dataset (idempotente).
const dataset = bigquery.dataset(datasetId);
const [datasetExists] = await dataset.exists();
if (!datasetExists) {
  await dataset.create({ location });
  console.log(`OK: dataset creado -> ${projectId}.${datasetId} (${location})`);
} else {
  console.log(`= dataset ya existe -> ${projectId}.${datasetId}`);
}

// 2) External table sobre el lake (create-or-replace para refrescar esquema/URIs).
const table = dataset.table(tableId);
const [tableExists] = await table.exists();
if (tableExists) {
  await table.delete();
  console.log(`= external table previa eliminada -> ${datasetId}.${tableId} (se recrea)`);
}

const sourceUri = `gs://${bucket}/raw/*.json`;
await dataset.createTable(tableId, {
  schema: { fields: schema },
  externalDataConfiguration: {
    sourceFormat: 'NEWLINE_DELIMITED_JSON',
    sourceUris: [sourceUri],
    ignoreUnknownValues: true, // tolerante a campos nuevos en el JSON sin romper la consulta
  },
});

console.log(`OK: external table -> ${datasetId}.${tableId}`);
console.log(`    fuente: ${sourceUri} (NEWLINE_DELIMITED_JSON)`);
console.log('Listo. Pruébalo:');
console.log(
  `    bq query --use_legacy_sql=false "SELECT deviceId, COUNT(*) c FROM \\\`${projectId}.${datasetId}.${tableId}\\\` GROUP BY deviceId ORDER BY c DESC LIMIT 10"`,
);
