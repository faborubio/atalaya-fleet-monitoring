# infra — Infraestructura de desarrollo

Reproduce el pipeline event-driven de Atalaya en local, sin AWS real (ADR-009).

## Servicios (docker-compose)

| Servicio | Imagen | Puerto | Rol |
|---|---|---|---|
| LocalStack | `localstack/localstack:3.7` | 4566 | SNS (fan-out) + SQS (buffer + DLQ) + S3 (data lake) |
| Redis | `redis:7-alpine` | 6379 | Set de dedup (ADR-006) + backplane SignalR (ADR-002) |
| Postgres | `postgres:16-alpine` | 5432 | Read models + telemetría (ADR-005/007) |

> **LocalStack:** usa una versión **3.x (community, gratis)**. NO uses `latest`: apunta a
> builds 2026.x que exigen `LOCALSTACK_AUTH_TOKEN` (edición pro).

## Uso

```bash
docker compose -f infra/docker-compose.yml up -d      # levantar
docker compose -f infra/docker-compose.yml ps         # estado (healthy)
docker compose -f infra/docker-compose.yml down       # parar (conserva datos)
docker compose -f infra/docker-compose.yml down -v    # parar y borrar datos (pgdata)
```

## Recursos AWS (creados por `localstack/init/01-resources.sh`)

- **SNS topic:** `atalaya-telemetry`
- **SQS:** `atalaya-telemetry-queue` (con redrive a `atalaya-telemetry-dlq`, maxReceiveCount=5)
- **S3 bucket:** `atalaya-datalake`
- Suscripción SNS→SQS con `RawMessageDelivery=true`

Inspección rápida (vía `awslocal` dentro del contenedor):
```bash
docker exec atalaya-dev-localstack-1 awslocal sqs list-queues
docker exec atalaya-dev-localstack-1 awslocal sns list-topics
docker exec atalaya-dev-localstack-1 awslocal s3 ls
```

## Conexión desde los servicios .NET (endpoints dev)

| Recurso | Endpoint |
|---|---|
| AWS (LocalStack) | `http://localhost:4566` (region `us-east-1`, credenciales dummy `test`/`test`) |
| Redis | `localhost:6379` |
| Postgres | `Host=localhost;Port=5432;Database=atalaya;Username=atalaya;Password=atalaya` |

> **Objetivo (ADR-009):** estos recursos los definirá **AWS CDK**; el `01-resources.sh`
> con `awslocal` es el atajo de dev para tener el pipeline corriendo ya.
