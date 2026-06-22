import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { appRoutes } from './app.routes';
import { FleetStore } from './core/telemetry/fleet-store';
import { AlertStore } from './core/telemetry/alert-store';

/** Stubs de los stores: evitan abrir SignalR/HTTP en tests del shell. */
const fleetStub: Partial<FleetStore> = {
  live: signal(false),
  count: signal(0) as unknown as FleetStore['count'],
  eventsPerSec: signal(0),
  start: async () => void 0,
};

const alertStub: Partial<AlertStore> = {
  criticalCount: signal(0) as unknown as AlertStore['criticalCount'],
  start: () => void 0,
};

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(appRoutes),
        { provide: FleetStore, useValue: fleetStub },
        { provide: AlertStore, useValue: alertStub },
      ],
    }).compileComponents();
  });

  it('debe renderizar la marca Atalaya', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.shell__brand')?.textContent).toContain(
      'Atalaya'
    );
  });

  it('debe pintar la navegación de las 4 features', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const links = compiled.querySelectorAll('.shell__nav a');
    expect(links.length).toBe(4);
  });
});
