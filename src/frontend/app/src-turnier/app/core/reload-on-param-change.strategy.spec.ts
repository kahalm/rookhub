import { ActivatedRouteSnapshot, Route } from '@angular/router';
import { ReloadOnParamChangeStrategy } from './reload-on-param-change.strategy';

describe('ReloadOnParamChangeStrategy', () => {
  const strategy = new ReloadOnParamChangeStrategy();
  const snapshot = (routeConfig: Route | null, params: Record<string, string>): ActivatedRouteSnapshot =>
    ({ routeConfig, params } as unknown as ActivatedRouteSnapshot);

  const detail: Route = { path: 'tournaments/:id', data: { reloadOnParamChange: true } };
  const other: Route = { path: 'tournaments/calendar/:id' };

  /** Die Gruppen-Umschaltung: gleiche Route, andere Id → neu aufbauen, sonst bliebe das alte Turnier stehen. */
  it('baut eine markierte Route bei anderer Id neu auf', () => {
    expect(strategy.shouldReuseRoute(snapshot(detail, { id: '27' }), snapshot(detail, { id: '26' }))).toBeFalse();
  });

  it('benutzt sie bei gleicher Id weiter (z. B. nur ein anderer Reiter in der Adresse)', () => {
    expect(strategy.shouldReuseRoute(snapshot(detail, { id: '26' }), snapshot(detail, { id: '26' }))).toBeTrue();
  });

  it('laesst unmarkierte Routen beim Angular-Verhalten', () => {
    expect(strategy.shouldReuseRoute(snapshot(other, { id: '2' }), snapshot(other, { id: '1' }))).toBeTrue();
    expect(strategy.shouldReuseRoute(snapshot(other, { id: '1' }), snapshot(detail, { id: '1' }))).toBeFalse();
  });
});
