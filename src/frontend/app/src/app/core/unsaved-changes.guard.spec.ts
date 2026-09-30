import { of } from 'rxjs';
import { routes } from '../app.routes';
import { LeaveConfirm, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  const run = (c: LeaveConfirm | null) => unsavedChangesGuard(c as LeaveConfirm, {} as any, {} as any, {} as any);

  it('reicht die Antwort der Seite durch', () => {
    expect(run({ canLeave: () => true })).toBe(true);
    const answer = of(false);
    expect(run({ canLeave: () => answer })).toBe(answer);
  });

  it('lässt Seiten ohne canLeave (oder ohne Komponente) einfach gehen', () => {
    expect(run({} as LeaveConfirm)).toBe(true);
    expect(run(null)).toBe(true);
  });

  // W3 F4-003: die Partie-Korrektur verlor ihren Arbeitsstand ohne Rückfrage.
  it('hängt an der Partie-Korrektur', () => {
    expect(routes.find(r => r.path === 'games/:id/edit')?.canDeactivate).toContain(unsavedChangesGuard);
  });
});
