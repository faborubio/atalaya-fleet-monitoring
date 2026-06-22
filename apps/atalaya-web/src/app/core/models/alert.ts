/** Severidad de alerta (espejo de Atalaya.Contracts.AlertSeverity; serializa como string). */
export type AlertSeverity = 'Warning' | 'Critical';

/** Alerta por umbral (espejo de Atalaya.Contracts.Alert). */
export interface Alert {
  alertId: string;
  deviceId: string;
  rule: string;
  severity: AlertSeverity;
  value: number;
  ts: string;
  message: string;
}
