// Asigna el custom claim "role" a un usuario de Identity Platform / Firebase Auth (G3, ADR-013).
// El backend lee ese claim para la RBAC (política `read` = operador|admin). No hay UI de consola
// para custom claims, por eso este script (one-off por usuario).
//
// Requisitos:
//   - npm install  (trae firebase-admin como devDependency)
//   - Credenciales de admin del proyecto, por una de estas vías:
//       a) ruta a una service account key como 4º argumento (recomendado, sin gcloud)
//          Firebase console -> Project settings -> Service accounts -> Generate new private key
//       b) gcloud auth application-default login   (si tienes gcloud)
//       c) $env:GOOGLE_APPLICATION_CREDENTIALS = "ruta\\serviceAccountKey.json"
//
// Uso:
//   node scripts/set-role.mjs <projectId> <email> <operador|admin> [ruta\a\serviceAccountKey.json]
//
// ⚠️ La service account key es un SECRETO: guárdala FUERA del repo, no la subas a git.
// El usuario debe volver a iniciar sesión (o refrescar el ID token) para que el claim aplique.

import { initializeApp, applicationDefault, cert } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';

const [projectId, email, role, keyPath] = process.argv.slice(2);

if (!projectId || !email || !role) {
  console.error('Uso: node scripts/set-role.mjs <projectId> <email> <operador|admin> [keyPath.json]');
  process.exit(1);
}
if (role !== 'operador' && role !== 'admin') {
  console.error('Error: role debe ser "operador" o "admin".');
  process.exit(1);
}

const credential = keyPath ? cert(keyPath) : applicationDefault();
initializeApp({ credential, projectId });

const auth = getAuth();
const user = await auth.getUserByEmail(email);
await auth.setCustomUserClaims(user.uid, { role });

console.log(`OK: ${email} (uid ${user.uid}) -> role=${role}`);
console.log('El usuario debe re-loguearse (o refrescar el ID token) para que el claim aplique.');
