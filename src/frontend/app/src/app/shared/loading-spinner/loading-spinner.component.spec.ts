import { TestBed } from '@angular/core/testing';
import { LoadingSpinnerComponent } from './loading-spinner.component';

describe('LoadingSpinnerComponent', () => {
  it('renders', async () => {
    await TestBed.configureTestingModule({ imports: [LoadingSpinnerComponent] }).compileComponents();
    const f = TestBed.createComponent(LoadingSpinnerComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });
});
