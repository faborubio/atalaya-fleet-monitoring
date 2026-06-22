#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { AtalayaStack } from '../lib/atalaya-stack';

/**
 * App CDK de Atalaya (ADR-009). Define la infra event-driven como código, reemplazando el
 * atajo `infra/localstack/init/01-resources.sh`. Los nombres físicos se fijan para que los
 * servicios .NET (que resuelven por nombre) funcionen igual contra LocalStack o AWS real.
 *
 *   cdk synth            → CloudFormation (offline, sin cuenta)
 *   cdklocal deploy      → despliega contra LocalStack (community)
 */
const app = new cdk.App();

new AtalayaStack(app, 'AtalayaStack', {
  description: 'Atalaya — pipeline de ingesta event-driven (SNS→SQS) + data lake S3 (ADR-001/007).',
  // Stack agnóstico de entorno: synth no requiere credenciales; cdklocal apunta a LocalStack.
});

app.synth();
