/** Severidad de alerta (espejo de Atalaya.Contracts.AlertSeverity; serializa como string). */
export type AlertSeverity = 'Warning' | 'Critical';

/** Estado de un incidente (espejo de Atalaya.Contracts.IncidentStatus). */
export type IncidentStatus = 'Open' | 'Resolved';

/** Incidente de alerta (espejo de Atalaya.Contracts.AlertIncident, AUD-016/p1). */
export interface AlertIncident {
  incidentId: string;
  deviceId: string;
  rule: string;
  severity: AlertSeverity;
  status: IncidentStatus;
  value: number;
  openedAt: string;
  updatedAt: string;
  message: string;
}
