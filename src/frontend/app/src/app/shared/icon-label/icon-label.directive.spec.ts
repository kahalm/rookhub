import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatTooltip } from '@angular/material/tooltip';
import { IconLabelDirective } from './icon-label.directive';

@Component({
  standalone: true,
  imports: [IconLabelDirective],
  template: `<button type="button" [appIconLabel]="label()"><span aria-hidden="true">↻</span></button>`,
})
class HostComponent {
  readonly label = signal('Brett drehen');
}

/** Codereview UX-014: ein Text → Tooltip UND zugänglicher Name (vorher nur Tooltip = Knopf ohne Namen). */
describe('IconLabelDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let button: HTMLButtonElement;
  let tooltip: MatTooltip;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const el = fixture.debugElement.query(By.css('button'));
    button = el.nativeElement;
    tooltip = el.injector.get(MatTooltip);
  });

  it('setzt aria-label und Tooltip aus demselben Text', () => {
    expect(button.getAttribute('aria-label')).toBe('Brett drehen');
    expect(tooltip.message).toBe('Brett drehen');
  });

  it('beschreibt den Knopf nicht noch einmal mit demselben Text (kein doppeltes Vorlesen)', () => {
    expect(button.hasAttribute('aria-describedby')).toBeFalse();
  });

  it('folgt einem geänderten Text (Sprachwechsel, umschaltender Knopf)', async () => {
    fixture.componentInstance.label.set('Flip board');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(button.getAttribute('aria-label')).toBe('Flip board');
    expect(tooltip.message).toBe('Flip board');
  });

  it('leerer Text → kein leeres aria-label', async () => {
    fixture.componentInstance.label.set('');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(button.hasAttribute('aria-label')).toBeFalse();
    expect(tooltip.message).toBe('');
  });
});
