import { demoApiConfig, type ApiConfig } from './api.config';
import { demoAuthConfig, type AuthConfig } from './auth/auth.config';

/**
 * Build de **demo de portafolio** (ADR-014, Nivel 1). Reemplaza a `deploy-target.ts` en la
 * configuración `demo` (`nx build atalaya-web --configuration=demo`): apunta a la API InMemory de
 * demo (Cloud Run scale-to-zero) y usa el login visible de un clic (`Auth:Dev` en el backend).
 */
export const apiConfig: ApiConfig = demoApiConfig;
export const authConfig: AuthConfig = demoAuthConfig;
