import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ResetLinkDialogComponent, ResetLinkDialogData } from './reset-link-dialog.component';
import { SnackbarService } from '../../core/snackbar.service';

describe('ResetLinkDialogComponent', () => {
  const data: ResetLinkDialogData = {
    username: 'spieler',
    link: 'https://rookhub.example/reset-password?token=abc',
    expiresAt: '2026-10-25T08:00:00Z',
    hasEmail: false,
  };
  let snackbar: { copy: jasmine.Spy };

  async function render(d: ResetLinkDialogData = data) {
    snackbar = { copy: jasmine.createSpy('copy') };
    await TestBed.configureTestingModule({
      imports: [ResetLinkDialogComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: d },
        { provide: SnackbarService, useValue: snackbar },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ResetLinkDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt den Link read-only und die Hinweise (ohne E-Mail)', async () => {
    const fixture = await render();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input.link-input');
    expect(input.value).toBe(data.link);
    expect(input.readOnly).toBeTrue();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('admin.users.resetLink.validity');
    expect(text).toContain('admin.users.resetLink.selfService');
    expect(text).toContain('admin.users.resetLink.noEmail');
  });

  it('nennt die vorhandene E-Mail-Adresse', async () => {
    const fixture = await render({ ...data, hasEmail: true });
    expect(fixture.nativeElement.textContent).toContain('admin.users.resetLink.hasEmail');
  });

  it('Kopieren schreibt den Link in die Zwischenablage', async () => {
    const fixture = await render();
    const write = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText: write } as any);

    fixture.componentInstance.copyLink();
    await Promise.resolve();
    await Promise.resolve();

    expect(write).toHaveBeenCalledWith(data.link);
    expect(snackbar.copy).toHaveBeenCalledWith('admin.users.resetLink.copied');
  });
});
