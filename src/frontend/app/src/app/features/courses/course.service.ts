import { Injectable, inject } from '@angular/core';
import { CourseApi } from './course-api';
import { CourseAuthoringApi } from './course-authoring-api';
import { CourseShareApi } from './course-share-api';
import { CourseTranslationApi } from './course-translation-api';

// Die Typen wohnen in course.types.ts; hier weitergereicht, damit die vielen Aufrufer ihren Import behalten.
export * from './course.types';

/**
 * Fassade der Kurs-Schnittstelle (`/api/courses`, Codereview F3-020 / Zerlegung Z33). Die Zugriffe selbst
 * stehen nach Thema in vier Diensten — {@link CourseApi} (lesen/lösen), {@link CourseAuthoringApi} (anlegen/pflegen),
 * {@link CourseShareApi} (teilen/öffentlich) und {@link CourseTranslationApi} (Übersetzung). Die Fassade bleibt, weil
 * rund 35 Komponenten und ihre Specs `CourseService` injizieren bzw. ersetzen; neue Aufrufer nehmen den passenden Dienst.
 */
@Injectable({ providedIn: 'root' })
export class CourseService {
  private readonly course = inject(CourseApi);
  private readonly authoring = inject(CourseAuthoringApi);
  private readonly share = inject(CourseShareApi);
  private readonly translation = inject(CourseTranslationApi);

  /** Siehe {@link CourseApi.accessChanged$}. */
  readonly accessChanged$ = this.course.accessChanged$;

  // ---- CourseApi ----
  notifyAccessChanged(...a: Parameters<CourseApi['notifyAccessChanged']>): ReturnType<CourseApi['notifyAccessChanged']> { return this.course.notifyAccessChanged(...a); }
  getCourses(...a: Parameters<CourseApi['getCourses']>): ReturnType<CourseApi['getCourses']> { return this.course.getCourses(...a); }
  checkAccess(...a: Parameters<CourseApi['checkAccess']>): ReturnType<CourseApi['checkAccess']> { return this.course.checkAccess(...a); }
  getDetail(...a: Parameters<CourseApi['getDetail']>): ReturnType<CourseApi['getDetail']> { return this.course.getDetail(...a); }
  getChapters(...a: Parameters<CourseApi['getChapters']>): ReturnType<CourseApi['getChapters']> { return this.course.getChapters(...a); }
  getBookPuzzles(...a: Parameters<CourseApi['getBookPuzzles']>): ReturnType<CourseApi['getBookPuzzles']> { return this.course.getBookPuzzles(...a); }
  getLineStatus(...a: Parameters<CourseApi['getLineStatus']>): ReturnType<CourseApi['getLineStatus']> { return this.course.getLineStatus(...a); }
  getNext(...a: Parameters<CourseApi['getNext']>): ReturnType<CourseApi['getNext']> { return this.course.getNext(...a); }
  recordResult(...a: Parameters<CourseApi['recordResult']>): ReturnType<CourseApi['recordResult']> { return this.course.recordResult(...a); }
  retryFailed(...a: Parameters<CourseApi['retryFailed']>): ReturnType<CourseApi['retryFailed']> { return this.course.retryFailed(...a); }
  setSkipInfoLines(...a: Parameters<CourseApi['setSkipInfoLines']>): ReturnType<CourseApi['setSkipInfoLines']> { return this.course.setSkipInfoLines(...a); }
  reset(...a: Parameters<CourseApi['reset']>): ReturnType<CourseApi['reset']> { return this.course.reset(...a); }
  resetChapter(...a: Parameters<CourseApi['resetChapter']>): ReturnType<CourseApi['resetChapter']> { return this.course.resetChapter(...a); }
  markInfoSeen(...a: Parameters<CourseApi['markInfoSeen']>): ReturnType<CourseApi['markInfoSeen']> { return this.course.markInfoSeen(...a); }
  pinCourse(...a: Parameters<CourseApi['pinCourse']>): ReturnType<CourseApi['pinCourse']> { return this.course.pinCourse(...a); }
  unpinCourse(...a: Parameters<CourseApi['unpinCourse']>): ReturnType<CourseApi['unpinCourse']> { return this.course.unpinCourse(...a); }
  getFlashcardMarks(...a: Parameters<CourseApi['getFlashcardMarks']>): ReturnType<CourseApi['getFlashcardMarks']> { return this.course.getFlashcardMarks(...a); }
  setFlashcardMark(...a: Parameters<CourseApi['setFlashcardMark']>): ReturnType<CourseApi['setFlashcardMark']> { return this.course.setFlashcardMark(...a); }
  getEbookRefs(...a: Parameters<CourseApi['getEbookRefs']>): ReturnType<CourseApi['getEbookRefs']> { return this.course.getEbookRefs(...a); }
  downloadPgn(...a: Parameters<CourseApi['downloadPgn']>): ReturnType<CourseApi['downloadPgn']> { return this.course.downloadPgn(...a); }
  downloadChapterPgn(...a: Parameters<CourseApi['downloadChapterPgn']>): ReturnType<CourseApi['downloadChapterPgn']> { return this.course.downloadChapterPgn(...a); }
  downloadLinePgn(...a: Parameters<CourseApi['downloadLinePgn']>): ReturnType<CourseApi['downloadLinePgn']> { return this.course.downloadLinePgn(...a); }

  // ---- CourseAuthoringApi ----
  createCourse(...a: Parameters<CourseAuthoringApi['createCourse']>): ReturnType<CourseAuthoringApi['createCourse']> { return this.authoring.createCourse(...a); }
  deleteCourse(...a: Parameters<CourseAuthoringApi['deleteCourse']>): ReturnType<CourseAuthoringApi['deleteCourse']> { return this.authoring.deleteCourse(...a); }
  setCourseThemes(...a: Parameters<CourseAuthoringApi['setCourseThemes']>): ReturnType<CourseAuthoringApi['setCourseThemes']> { return this.authoring.setCourseThemes(...a); }
  setCalculation(...a: Parameters<CourseAuthoringApi['setCalculation']>): ReturnType<CourseAuthoringApi['setCalculation']> { return this.authoring.setCalculation(...a); }
  linkCourse(...a: Parameters<CourseAuthoringApi['linkCourse']>): ReturnType<CourseAuthoringApi['linkCourse']> { return this.authoring.linkCourse(...a); }
  getLink(...a: Parameters<CourseAuthoringApi['getLink']>): ReturnType<CourseAuthoringApi['getLink']> { return this.authoring.getLink(...a); }
  unlinkCourse(...a: Parameters<CourseAuthoringApi['unlinkCourse']>): ReturnType<CourseAuthoringApi['unlinkCourse']> { return this.authoring.unlinkCourse(...a); }
  linkEbook(...a: Parameters<CourseAuthoringApi['linkEbook']>): ReturnType<CourseAuthoringApi['linkEbook']> { return this.authoring.linkEbook(...a); }
  convertToRepertoire(...a: Parameters<CourseAuthoringApi['convertToRepertoire']>): ReturnType<CourseAuthoringApi['convertToRepertoire']> { return this.authoring.convertToRepertoire(...a); }
  getChapterLines(...a: Parameters<CourseAuthoringApi['getChapterLines']>): ReturnType<CourseAuthoringApi['getChapterLines']> { return this.authoring.getChapterLines(...a); }
  addLines(...a: Parameters<CourseAuthoringApi['addLines']>): ReturnType<CourseAuthoringApi['addLines']> { return this.authoring.addLines(...a); }
  deleteLine(...a: Parameters<CourseAuthoringApi['deleteLine']>): ReturnType<CourseAuthoringApi['deleteLine']> { return this.authoring.deleteLine(...a); }
  renameChapter(...a: Parameters<CourseAuthoringApi['renameChapter']>): ReturnType<CourseAuthoringApi['renameChapter']> { return this.authoring.renameChapter(...a); }
  deleteChapter(...a: Parameters<CourseAuthoringApi['deleteChapter']>): ReturnType<CourseAuthoringApi['deleteChapter']> { return this.authoring.deleteChapter(...a); }
  reprocessStatus(...a: Parameters<CourseAuthoringApi['reprocessStatus']>): ReturnType<CourseAuthoringApi['reprocessStatus']> { return this.authoring.reprocessStatus(...a); }
  reprocess(...a: Parameters<CourseAuthoringApi['reprocess']>): ReturnType<CourseAuthoringApi['reprocess']> { return this.authoring.reprocess(...a); }

  // ---- CourseShareApi ----
  shareCourse(...a: Parameters<CourseShareApi['shareCourse']>): ReturnType<CourseShareApi['shareCourse']> { return this.share.shareCourse(...a); }
  getShareRecipients(...a: Parameters<CourseShareApi['getShareRecipients']>): ReturnType<CourseShareApi['getShareRecipients']> { return this.share.getShareRecipients(...a); }
  unshareCourse(...a: Parameters<CourseShareApi['unshareCourse']>): ReturnType<CourseShareApi['unshareCourse']> { return this.share.unshareCourse(...a); }
  getPublicCourse(...a: Parameters<CourseShareApi['getPublicCourse']>): ReturnType<CourseShareApi['getPublicCourse']> { return this.share.getPublicCourse(...a); }
  resolvePublicSlug(...a: Parameters<CourseShareApi['resolvePublicSlug']>): ReturnType<CourseShareApi['resolvePublicSlug']> { return this.share.resolvePublicSlug(...a); }
  resolvePublicSlugChapter(...a: Parameters<CourseShareApi['resolvePublicSlugChapter']>): ReturnType<CourseShareApi['resolvePublicSlugChapter']> { return this.share.resolvePublicSlugChapter(...a); }

  // ---- CourseTranslationApi ----
  getTranslations(...a: Parameters<CourseTranslationApi['getTranslations']>): ReturnType<CourseTranslationApi['getTranslations']> { return this.translation.getTranslations(...a); }
  requestTranslation(...a: Parameters<CourseTranslationApi['requestTranslation']>): ReturnType<CourseTranslationApi['requestTranslation']> { return this.translation.requestTranslation(...a); }
  withdrawTranslation(...a: Parameters<CourseTranslationApi['withdrawTranslation']>): ReturnType<CourseTranslationApi['withdrawTranslation']> { return this.translation.withdrawTranslation(...a); }
}
