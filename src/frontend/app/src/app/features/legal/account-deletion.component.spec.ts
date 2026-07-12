import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AccountDeletionComponent } from './account-deletion.component';

describe('AccountDeletionComponent', () => {
  it('renders (template compiles)', async () => {
    await TestBed.configureTestingModule({
      imports: [AccountDeletionComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const f = TestBed.createComponent(AccountDeletionComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });
});
