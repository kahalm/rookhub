import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { ClubClient } from '../../core/club-api.service';
import { RosterPerson } from '../../core/club.models';
import { PlayerSearchComponent } from './player-search.component';

describe('PlayerSearchComponent', () => {
  let fixture: ComponentFixture<PlayerSearchComponent>;
  let client: jasmine.SpyObj<ClubClient>;

  beforeEach(() => {
    client = jasmine.createSpyObj<ClubClient>('ClubClient', ['players']);
    TestBed.configureTestingModule({ imports: [PlayerSearchComponent] });
    fixture = TestBed.createComponent(PlayerSearchComponent);
    fixture.componentRef.setInput('client', client);
    fixture.detectChanges();
  });

  const LIGA: RosterPerson = { name: 'Hengl, Philip', fide: '222', teams: ['Absam'], club: false, league: true, source: 'liga' };
  const MEGA: RosterPerson = { name: 'Hengl, Peter', fide: '777', teams: [], club: false, league: false, source: 'mega', games: 12, lastYear: 2019, maxElo: 1850 };

  it('sucht standardmäßig auch in der Megabase, ohne Häkchen nur in der Liga; ein Treffer bringt die FIDE-ID mit', fakeAsync(() => {
    const el = fixture.nativeElement as HTMLElement;
    client.players.and.resolveTo([LIGA]);
    const typed: string[] = [];
    fixture.componentInstance.textChange.subscribe(t => typed.push(t));
    fixture.componentInstance.setAll(false);
    fixture.componentInstance.onInput('heng');
    tick(300);
    flushMicrotasks();
    fixture.detectChanges();
    expect(client.players).toHaveBeenCalledWith('heng', false);
    expect(typed).toEqual(['heng']);
    expect(el.querySelector('.psearch-results')?.textContent).toContain('Ligaspieler');

    client.players.and.resolveTo([LIGA, MEGA]);
    fixture.componentInstance.setAll(true);
    tick(0);
    flushMicrotasks();
    fixture.detectChanges();
    expect(client.players).toHaveBeenCalledWith('heng', true);
    const rows = el.querySelectorAll('.psearch-results li');
    expect(rows[1].textContent).toContain('Megabase');
    expect(rows[1].textContent).toContain('FIDE 777 · 12 Partien · bis 2019 · Elo bis 1850');

    let picked: RosterPerson | null = null;
    fixture.componentInstance.picked.subscribe(p => picked = p);
    (rows[1].querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(picked!.fide).toBe('777');
    expect(el.querySelector('.psearch-results')).toBeNull();
  }));

  it('Megabase-Häkchen ist vorgegeben', () => {
    expect((fixture.nativeElement.querySelector('.psearch-all input') as HTMLInputElement).checked).toBeTrue();
  });

  it('beim Partieformular (searchMega=false) sucht es zuerst nur in der Liga', fakeAsync(() => {
    const f = TestBed.createComponent(PlayerSearchComponent);
    f.componentRef.setInput('client', client);
    f.componentRef.setInput('searchMega', false);
    f.detectChanges();
    expect((f.nativeElement.querySelector('.psearch-all input') as HTMLInputElement).checked).toBeFalse();
    client.players.and.resolveTo([LIGA]);
    f.componentInstance.onInput('heng');
    tick(300);
    flushMicrotasks();
    expect(client.players).toHaveBeenCalledWith('heng', false);
  }));

  it('Hineinklicken sucht gleich mit dem Namen, der schon dasteht', fakeAsync(() => {
    client.players.and.resolveTo([LIGA]);
    fixture.componentInstance.text = 'Hengl';
    (fixture.nativeElement.querySelector('.psearch input') as HTMLInputElement).dispatchEvent(new Event('focus'));
    tick(0);
    flushMicrotasks();
    fixture.detectChanges();
    expect(client.players).toHaveBeenCalledWith('Hengl', true);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.psearch-results li').length).toBe(1);
  }));

  it('ohne Treffer in der Liga der Hinweis aufs Häkchen', fakeAsync(() => {
    client.players.and.resolveTo([]);
    fixture.componentInstance.setAll(false);
    tick(0);
    fixture.componentInstance.onInput('zzz');
    tick(300);
    flushMicrotasks();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Häkchen setzen, um die Megabase zu durchsuchen');
  }));
});
