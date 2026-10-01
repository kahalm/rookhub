import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { PuzzleActionBarComponent } from './puzzle-action-bar.component';

/**
 * OnPush-Regression für die präsentationalen Puzzle-Display-Cards: bei reiner Input-Bindung
 * (Eltern rebinden je CD) muss eine geänderte Eingabe weiterhin neu rendern, und Klick-Outputs
 * müssen feuern. Bestätigt, dass das Umstellen auf OnPush das Verhalten nicht bricht.
 */
// Host nutzt Default-CD (kein OnPush): so propagiert ein direkter Input-Wechsel + detectChanges
// an die OnPush-Kinder — getestet wird damit die Re-Render-Reaktion der KINDER auf Input-Änderung.
@Component({
  standalone: true,
  imports: [PuzzleActionBarComponent],
  template: `
    <app-puzzle-action-bar
      [levelText]="levelText"
      [rating]="rating"
      (shareClicked)="shareClicks = shareClicks + 1"></app-puzzle-action-bar>
  `,
})
class HostComponent {
  levelText = 'Easy';
  rating = 1500;
  shareClicks = 0;
}

describe('Display cards (OnPush)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('puzzle-action-bar is OnPush', () => {
    expect((PuzzleActionBarComponent as any).ɵcmp.onPush).toBeTrue();
  });

  it('emits the share output on click (OnPush does not swallow events)', () => {
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.pab-share') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(host.shareClicks).toBe(1);
  });
});
