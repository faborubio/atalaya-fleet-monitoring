import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import * as sns from 'aws-cdk-lib/aws-sns';
import * as subs from 'aws-cdk-lib/aws-sns-subscriptions';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import * as s3 from 'aws-cdk-lib/aws-s3';

/**
 * Infra del camino caliente y frío de Atalaya como código (ADR-009). Equivalente 1:1 del script
 * `infra/localstack/init/01-resources.sh`:
 *  - SNS topic (fan-out) → SQS queue (buffer) con redrive a DLQ tras 5 intentos (ADR-001/006).
 *  - S3 bucket del data lake con lifecycle cold/archive (ADR-007).
 *
 * Nombres físicos fijos: los servicios .NET resuelven por nombre, así que esto vale igual contra
 * LocalStack (`cdklocal`) o AWS real sin tocar configuración.
 */
export class AtalayaStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Data lake de eventos crudos (ADR-007): inmutable + lifecycle a almacenamiento frío/archivo.
    const dataLake = new s3.Bucket(this, 'DataLake', {
      bucketName: 'atalaya-datalake',
      removalPolicy: cdk.RemovalPolicy.DESTROY, // dev; en prod: RETAIN
      enforceSSL: true,
      lifecycleRules: [
        {
          id: 'cold-then-archive',
          transitions: [
            { storageClass: s3.StorageClass.INFREQUENT_ACCESS, transitionAfter: cdk.Duration.days(30) },
            { storageClass: s3.StorageClass.GLACIER, transitionAfter: cdk.Duration.days(90) },
          ],
        },
      ],
    });

    // DLQ para mensajes envenenados (resiliencia, SAD §8).
    const dlq = new sqs.Queue(this, 'TelemetryDlq', {
      queueName: 'atalaya-telemetry-dlq',
      retentionPeriod: cdk.Duration.days(14),
    });

    // Cola principal (buffer que absorbe picos, ADR-001) con redrive a la DLQ tras 5 intentos.
    const queue = new sqs.Queue(this, 'TelemetryQueue', {
      queueName: 'atalaya-telemetry-queue',
      visibilityTimeout: cdk.Duration.seconds(30),
      deadLetterQueue: { queue: dlq, maxReceiveCount: 5 },
    });

    // Topic SNS (fan-out) + suscripción de la cola con RawMessageDelivery (el worker espera el
    // body crudo, no el sobre de SNS).
    const topic = new sns.Topic(this, 'TelemetryTopic', {
      topicName: 'atalaya-telemetry',
    });
    topic.addSubscription(new subs.SqsSubscription(queue, { rawMessageDelivery: true }));

    new cdk.CfnOutput(this, 'TopicArn', { value: topic.topicArn });
    new cdk.CfnOutput(this, 'QueueUrl', { value: queue.queueUrl });
    new cdk.CfnOutput(this, 'DlqUrl', { value: dlq.queueUrl });
    new cdk.CfnOutput(this, 'DataLakeBucket', { value: dataLake.bucketName });
  }
}
