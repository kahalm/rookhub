import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslateModule } from '@ngx-translate/core';
import { ShareCourseDialogComponent } from './share-course-dialog.component';
import { CourseService } from './course.service';
import { FriendsService } from '../../core/friends.service';
import { SnackbarService } from '../../core/snackbar.service';

describe('ShareCourseDialogComponent', () => {
  let fixture: ComponentFixture<ShareCourseDialogComponent>;
  let component: ShareCourseDialogComponent;
  let courseService: jasmine.SpyObj<CourseService>;
  let friendsService: jasmine.SpyObj<FriendsService>;
  const dialogRef = { close: jasmine.createSpy('close') };

  const friend = (userId: number, username: string) => ({
    friendshipId: userId, userId, username, displayName: null,
    chessComUsername: null, lichessUsername: null, fideId: null, chessResultsId: null,
  });

  beforeEach(async () => {
    courseService = jasmine.createSpyObj('CourseService', ['getShareRecipients', 'shareCourse', 'unshareCourse']);
    friendsService = jasmine.createSpyObj('FriendsService', ['getFriends']);
    friendsService.getFriends.and.returnValue(of([friend(2, 'alice'), friend(3, 'bob')]));
    courseService.getShareRecipients.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [ShareCourseDialogComponent, TranslateModule.forRoot()],
      providers: [
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: { bookId: 10, courseName: 'X' } },
        { provide: CourseService, useValue: courseService },
        { provide: FriendsService, useValue: friendsService },
        { provide: SnackbarService, useValue: jasmine.createSpyObj('SnackbarService', ['info', 'success']) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ShareCourseDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads friends and recipients on init', () => {
    expect(component.loading).toBeFalse();
    expect(component.friends.length).toBe(2);
    expect(component.selectableFriends.length).toBe(2);
  });

  it('excludes already-shared friends from the selectable list', () => {
    courseService.getShareRecipients.and.returnValue(of([{ userId: 2, username: 'alice', displayName: null, sharedAt: '' }]));
    const f = TestBed.createComponent(ShareCourseDialogComponent);
    f.detectChanges();
    expect(f.componentInstance.selectableFriends.map(x => x.userId)).toEqual([3]);
  });

  it('shares selected friends and moves them to recipients', () => {
    courseService.shareCourse.and.returnValue(of({ shared: 1, skipped: [] }));
    component.toggle(2, true);
    component.share();
    expect(courseService.shareCourse).toHaveBeenCalledWith(10, [2]);
    expect(component.recipients.some(r => r.userId === 2)).toBeTrue();
    expect(component.selected.size).toBe(0);
  });

  it('unshares a recipient', () => {
    component.recipients = [{ userId: 2, username: 'alice', displayName: null, sharedAt: '' }];
    courseService.unshareCourse.and.returnValue(of(void 0));
    component.unshare(component.recipients[0]);
    expect(courseService.unshareCourse).toHaveBeenCalledWith(10, 2);
    expect(component.recipients.length).toBe(0);
  });

  it('closes when loading fails', () => {
    friendsService.getFriends.and.returnValue(throwError(() => new Error('x')));
    const f = TestBed.createComponent(ShareCourseDialogComponent);
    f.detectChanges();
    expect(dialogRef.close).toHaveBeenCalled();
  });
});
