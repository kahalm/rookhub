import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { LockUserDialogComponent } from './lock-user-dialog.component';
import { lockEndFromDate, todayIso } from './lock-user.util';

/** Codereview P-4: „Sperren …“-Dialog — bis einschließlich eines Tages oder unbefristet, Grund optional. */
describe('LockUserDialogComponent', () => {
  function render() {
    const close = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [LockUserDialogComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { username: 'spam' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(LockUserDialogComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, close, el: fixture.nativeElement as HTMLElement };
  }

  it('Vorgabe: bis zu einem Tag (in sieben Tagen); Sperren liefert dessen Ende und den getrimmten Grund', () => {
    const { c, close, el } = render();
    expect(el.querySelector('input[type=date]')).not.toBeNull();
    c.reason.set('  Spam ');
    c.submit();
    expect(close).toHaveBeenCalledWith({ until: lockEndFromDate(c.day()), reason: 'Spam' });
  });

  it('unbefristet liefert until = null, ohne Datumsfeld', () => {
    const { c, close, el, fixture } = render();
    (el.querySelector('input[value=indefinite]') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(el.querySelector('input[type=date]')).toBeNull();
    c.submit();
    expect(close).toHaveBeenCalledWith({ until: null, reason: '' });
  });

  it('ein Tag vor heute oder ein leeres Feld schließt nicht, sondern meldet sich', () => {
    const { c, close, el, fixture } = render();
    c.day.set('2000-01-01');
    c.submit();
    c.day.set('');
    c.submit();
    fixture.detectChanges();
    expect(close).not.toHaveBeenCalled();
    expect(el.querySelector('[role=alert]')!.textContent).toContain('admin.users.lock.invalidDate');
    c.day.set(todayIso());
    c.submit();
    expect(close).toHaveBeenCalled();
  });
});
