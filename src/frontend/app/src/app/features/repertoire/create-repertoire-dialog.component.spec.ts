import { TestBed } from '@angular/core/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { CreateRepertoireDialogComponent } from './create-repertoire-dialog.component';
import { Repertoire } from '../../core/models';

describe('CreateRepertoireDialogComponent', () => {
  function build(data: Repertoire | null): CreateRepertoireDialogComponent {
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    return TestBed.runInInjectionContext(() => new CreateRepertoireDialogComponent(
      TestBed.inject(MatDialogRef),
      TestBed.inject(MAT_DIALOG_DATA),
    ));
  }

  it('neue Repertoires sind standardmäßig für die Extension aktiviert', () => {
    const c = build(null);
    expect(c.editMode).toBeFalse();
    expect(c.useForExtension).toBeTrue();
  });

  it('übernimmt useForExtension aus den Bearbeiten-Daten', () => {
    const rep = { id: 1, name: 'R', description: null, isPublic: false, kind: 1,
      fileCount: 0, useForExtension: false, createdAt: '', updatedAt: '' } as Repertoire;
    const c = build(rep);
    expect(c.editMode).toBeTrue();
    expect(c.useForExtension).toBeFalse();
  });
});
