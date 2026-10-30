import { Pipe, PipeTransform } from '@angular/core';
import { DirectoryEntry, displayName } from './tournament-directory.model';

/** Turniername zur Anzeige — von chess-results gekappte Namen enden mit „…" (d9-t-kartentitel-ohne-ellipse). */
@Pipe({ name: 'directoryName', standalone: true })
export class DirectoryNamePipe implements PipeTransform {
  transform(entry: Pick<DirectoryEntry, 'name' | 'chessResultsId'>): string {
    return displayName(entry);
  }
}
