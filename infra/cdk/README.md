# infra/cdk — Infraestructura como código (AWS CDK)

Define la infra event-driven de Atalaya como código (**ADR-009**), reemplazando el atajo
`infra/localstack/init/01-resources.sh`. Mismo resultado, pero versionado, revisable y
desplegable a AWS real o a LocalStack.

## Qué define (`lib/atalaya-stack.ts`)

| Recurso | Detalle |
|---|---|
| **SNS topic** `atalaya-telemetry` | fan-out de ingesta (ADR-001) |
| **SQS** `atalaya-telemetry-queue` | buffer; redrive a la DLQ tras 5 intentos (ADR-006) |
| **SQS** `atalaya-telemetry-dlq` | mensajes envenenados (retención 14 d) |
| Suscripción SNS→SQS | `RawMessageDelivery=true` (el worker espera el body crudo) |
| **S3** `atalaya-datalake` | data lake con lifecycle → IA (30 d) → Glacier (90 d), ADR-007 |

Los **nombres físicos son fijos**: los servicios .NET resuelven por nombre, así que esto
funciona igual contra LocalStack o AWS real sin tocar configuración.

## Uso

Requiere Node + Docker (para LocalStack). Las dependencias son locales a esta carpeta.

```bash
cd infra/cdk
npm install

# 1) Sintetizar CloudFormation (offline, sin cuenta AWS, sin costo)
npm run synth          # = cdk synth

# 2) Desplegar contra LocalStack (community, gratis) — requiere LocalStack en :4566
npm run deploy:local   # = cdklocal bootstrap && cdklocal deploy
npm run destroy:local
```

Para AWS **real** se usaría `cdk deploy` con credenciales (no cubierto en dev; ver DEPLOY.md).

> **Versiones:** la CLI nueva de aws-cdk (2.1xxx) exige **aws-cdk-local 3.x**
> ([TROUBLESHOOTING TS-007](../../TROUBLESHOOTING.md)). Con la combinación de `package.json`
> el deploy a LocalStack queda verificado (stack `AtalayaStack`, 9 recursos).

## Relación con el `docker-compose`

El `docker-compose` + `01-resources.sh` sigue siendo el **atajo de dev de cero fricción**
(provisiona al arrancar). El CDK es la **definición de infra productiva** y la fuente de verdad
de ADR-009; `cdklocal` permite probarla contra LocalStack con paridad.
