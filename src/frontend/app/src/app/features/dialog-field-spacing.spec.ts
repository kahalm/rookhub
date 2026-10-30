import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

/**
 * Abstände um Outline-Felder (DESIGN 5.2 / 5.6, d9-rk-dialog-hinweis-eng / d9-rk-wochenpost-karte-titel-eng): die
 * schwebende Beschriftung ragt ~8 px über den Feldrahmen. Gemessen wird gegen die echten globalen Regeln aus
 * styles.scss (Karma lädt sie mit) — Hinweis → nächste Beschriftung und Kartenüberschrift → erste Beschriftung je ≥ 12 px.
 */
@Component({
  standalone: true,
  imports: [MatFormFieldModule, MatInputModule, MatCardModule],
  template: `
    <div class="mat-mdc-dialog-content" style="display: flex; flex-direction: column; gap: 8px; width: 400px">
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="with-hint">
        <mat-label>Bereich</mat-label>
        <input matInput>
        <mat-hint>Ein Token gilt nur für seinen Bereich: ein Extension-Token kann keine Engines anmelden.</mat-hint>
      </mat-form-field>
      <mat-form-field appearance="outline" floatLabel="always" class="next">
        <mat-label>Ablauf</mat-label>
        <input matInput>
      </mat-form-field>
      <mat-form-field appearance="outline" class="no-hint">
        <mat-label>Ohne Hinweis</mat-label>
        <input matInput>
      </mat-form-field>
    </div>
    <mat-card style="width: 600px">
      <mat-card-header><mat-card-title class="card-title">Neuen Wochenpost hochladen</mat-card-title></mat-card-header>
      <mat-card-content>
        <div style="display: flex; gap: 12px">
          <mat-form-field appearance="outline" floatLabel="always" class="card-field">
            <mat-label>Datum</mat-label>
            <input matInput>
          </mat-form-field>
        </div>
      </mat-card-content>
    </mat-card>
  `,
})
class SpacingHost {}

describe('Abstände um Outline-Felder (DESIGN 5.2/5.6, d9)', () => {
  let el: HTMLElement;

  beforeEach(async () => {
    const fixture = TestBed.createComponent(SpacingHost);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    await fixture.whenStable();
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => el.remove());

  const label = (sel: string) => el.querySelector(`${sel} .mdc-floating-label`)!.getBoundingClientRect();

  it('Dialog: zwischen Hinweistext und der Beschriftung des nächsten Felds liegen ≥ 12 px', () => {
    const hint = el.querySelector('.with-hint .mat-mdc-form-field-hint')!.getBoundingClientRect();
    expect(label('.next').top - hint.bottom).toBeGreaterThanOrEqual(12);
  });

  it('Dialog: ein Feld ohne Hinweis bekommt keinen Zusatzabstand', () => {
    expect(getComputedStyle(el.querySelector('.no-hint')!).marginBottom).toBe('0px');
    expect(getComputedStyle(el.querySelector('.with-hint')!).marginBottom).toBe('12px');
  });

  it('Karte: die Beschriftung des ersten Felds hält ≥ 12 px Abstand zur Kartenüberschrift', () => {
    const title = el.querySelector('.card-title')!.getBoundingClientRect();
    expect(label('.card-field').top - title.bottom).toBeGreaterThanOrEqual(12);
  });
});
