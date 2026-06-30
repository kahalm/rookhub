import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { VizModeSelectorComponent } from './viz-mode-selector.component';

describe('VizModeSelectorComponent', () => {
  function create(mode = 0) {
    TestBed.configureTestingModule({
      imports: [VizModeSelectorComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations()],
    });
    const fixture = TestBed.createComponent(VizModeSelectorComponent);
    fixture.componentInstance.mode = mode;
    fixture.detectChanges();
    return fixture;
  }

  it('renders one button per visualization mode (0–4)', () => {
    const fixture = create();
    const btns = fixture.nativeElement.querySelectorAll('.vms-btn');
    expect(btns.length).toBe(5);
  });

  it('marks the current mode active', () => {
    const fixture = create(2);
    const btns = fixture.nativeElement.querySelectorAll('.vms-btn');
    expect(btns[2].classList).toContain('active');
    expect(btns[0].classList).not.toContain('active');
    expect(btns[2].getAttribute('aria-pressed')).toBe('true');
  });

  it('emits the chosen mode on click', () => {
    const fixture = create(0);
    let emitted = -1;
    fixture.componentInstance.modeChange.subscribe((m: number) => (emitted = m));
    const btns = fixture.nativeElement.querySelectorAll('.vms-btn');
    btns[3].click();
    expect(emitted).toBe(3);
  });
});
