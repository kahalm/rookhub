import { Component, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { installTouchSubmenuGuard, TOUCH_MOUSE_WINDOW_MS } from './touch-submenu-guard';

@Component({
  imports: [MatMenuModule],
  template: `
    <button [matMenuTriggerFor]="root">☰</button>
    <mat-menu #root="matMenu">
      <button mat-menu-item class="t-sub" [matMenuTriggerFor]="sub">Bibliothek</button>
      <button mat-menu-item class="t-other" [matMenuTriggerFor]="other">Training</button>
    </mat-menu>
    <mat-menu #sub="matMenu"><button mat-menu-item class="t-leaf">Partien</button></mat-menu>
    <mat-menu #other="matMenu"><button mat-menu-item>Puzzles</button></mat-menu>
  `,
})
class NestedMenuHost {
  readonly trigger = viewChild.required(MatMenuTrigger);
}

describe('installTouchSubmenuGuard', () => {
  let fixture: ComponentFixture<NestedMenuHost>;
  let overlay: HTMLElement;
  let clock: number;
  let uninstall: () => void;

  const item = (cls: string) => overlay.querySelector<HTMLElement>(cls)!;
  const panels = () => overlay.querySelectorAll('.mat-mdc-menu-panel').length;
  /** Offene Untermenüs nach Beschriftung ihres Eintrags (Panels im DOM zählen nicht: Schließ-Animation). */
  const openSubs = () => [...overlay.querySelectorAll<HTMLElement>('.mat-mdc-menu-item-submenu-trigger')]
    .filter(el => el.getAttribute('aria-expanded') === 'true').map(el => el.textContent!.trim());
  const touch = () => { document.dispatchEvent(new Event('touchstart')); };
  const enter = (el: HTMLElement) => { el.dispatchEvent(new MouseEvent('mouseenter')); fixture.detectChanges(); };
  const click = (el: HTMLElement) => { el.dispatchEvent(new MouseEvent('click', { bubbles: true })); fixture.detectChanges(); };

  beforeEach(() => {
    clock = 10_000;
    uninstall = installTouchSubmenuGuard(document, () => clock);
    fixture = TestBed.createComponent(NestedMenuHost);
    fixture.detectChanges();
    overlay = TestBed.inject(OverlayContainer).getContainerElement();
    fixture.componentInstance.trigger().openMenu();
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.componentInstance.trigger().closeMenu();
    fixture.detectChanges();
    uninstall();
  });

  it('opens a submenu on mouse hover as before', () => {
    enter(item('.t-sub'));
    expect(panels()).toBe(2);
  });

  it('does not open the submenu from the simulated hover of a tap — only the click opens it', () => {
    touch();
    enter(item('.t-sub'));
    expect(panels()).withContext('mouseenter right after touchstart').toBe(1);

    click(item('.t-sub'));
    expect(panels()).toBe(2);
    expect(item('.t-leaf')).not.toBeNull();
  });

  it('closes a sibling submenu when another trigger is tapped', () => {
    touch();
    click(item('.t-other'));
    expect(openSubs()).toEqual(['Training']);

    touch();
    enter(item('.t-sub'));
    click(item('.t-sub'));
    expect(openSubs()).toEqual(['Bibliothek']);
  });

  it('treats hover as mouse again once the touch is long past', () => {
    touch();
    clock += TOUCH_MOUSE_WINDOW_MS + 1;
    enter(item('.t-sub'));
    expect(panels()).toBe(2);
  });
});
