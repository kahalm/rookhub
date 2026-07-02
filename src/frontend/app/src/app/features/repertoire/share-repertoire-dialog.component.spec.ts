import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslateModule } from '@ngx-translate/core';
import { ShareRepertoireDialogComponent } from './share-repertoire-dialog.component';
import { RepertoireService } from '../../core/repertoire.service';
import { FriendsService } from '../../core/friends.service';
import { SnackbarService } from '../../core/snackbar.service';

describe('ShareRepertoireDialogComponent', () => {
  let fixture: ComponentFixture<ShareRepertoireDialogComponent>;
  let component: ShareRepertoireDialogComponent;
  let repertoireService: jasmine.SpyObj<RepertoireService>;
  let friendsService: jasmine.SpyObj<FriendsService>;
  const dialogRef = { close: jasmine.createSpy('close') };

  const friend = (userId: number, username: string) => ({
    friendshipId: userId, userId, username, displayName: null,
    chessComUsername: null, lichessUsername: null, fideId: null, chessResultsId: null,
  });

  beforeEach(async () => {
    repertoireService = jasmine.createSpyObj('RepertoireService', ['getShareRecipients', 'share', 'unshare']);
    friendsService = jasmine.createSpyObj('FriendsService', ['getFriends']);
    friendsService.getFriends.and.returnValue(of([friend(2, 'alice'), friend(3, 'bob')]));
    repertoireService.getShareRecipients.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [ShareRepertoireDialogComponent, TranslateModule.forRoot()],
      providers: [
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: { repertoireId: 10, repertoireName: 'X' } },
        { provide: RepertoireService, useValue: repertoireService },
        { provide: FriendsService, useValue: friendsService },
        { provide: SnackbarService, useValue: jasmine.createSpyObj('SnackbarService', ['info', 'success']) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ShareRepertoireDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads friends and recipients on init', () => {
    expect(component.loading).toBeFalse();
    expect(component.selectableFriends.length).toBe(2);
  });

  it('excludes already-shared friends', () => {
    repertoireService.getShareRecipients.and.returnValue(of([{ userId: 2, username: 'alice', displayName: null, sharedAt: '' }]));
    const f = TestBed.createComponent(ShareRepertoireDialogComponent);
    f.detectChanges();
    expect(f.componentInstance.selectableFriends.map(x => x.userId)).toEqual([3]);
  });

  it('shares selected friends and moves them to recipients', () => {
    repertoireService.share.and.returnValue(of({ shared: 1, skipped: [] }));
    component.toggle(2, true);
    component.share();
    expect(repertoireService.share).toHaveBeenCalledWith(10, [2]);
    expect(component.recipients.some(r => r.userId === 2)).toBeTrue();
  });

  it('unshares a recipient', () => {
    component.recipients = [{ userId: 2, username: 'alice', displayName: null, sharedAt: '' }];
    repertoireService.unshare.and.returnValue(of(void 0));
    component.unshare(component.recipients[0]);
    expect(repertoireService.unshare).toHaveBeenCalledWith(10, 2);
    expect(component.recipients.length).toBe(0);
  });

  it('closes when loading fails', () => {
    friendsService.getFriends.and.returnValue(throwError(() => new Error('x')));
    const f = TestBed.createComponent(ShareRepertoireDialogComponent);
    f.detectChanges();
    expect(dialogRef.close).toHaveBeenCalled();
  });
});
