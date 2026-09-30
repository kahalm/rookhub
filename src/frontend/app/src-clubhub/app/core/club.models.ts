/** ClubHub — Formen der API (`/api/club`, DTOs in `ClubDtos.cs`). */

export type ContactKind = 'phone' | 'email';

/** Ein Kontakt zu einem Kind: Nummer oder Adresse, mit Hinweis, wessen er ist („Mutter Daniela"). */
export interface Contact {
  kind: ContactKind;
  value: string;
  label?: string | null;
}

export interface GroupRef {
  id: number;
  name: string;
}

export interface MemberRow {
  id: number;
  firstName: string;
  /** Leer = nicht bekannt. */
  lastName: string;
  birthYear?: number | null;
  level?: string | null;
  archived: boolean;
  linked: boolean;
  groups: GroupRef[];
  contacts: Contact[];
}

export interface Note {
  id: number;
  text: string;
  createdAt: string;
  author?: string | null;
  canDelete: boolean;
}

/** Da oder nicht da — mehr hält die Anwesenheit nicht fest. */
export type Status = 'present' | 'absent';

export interface AttendanceEntry {
  sessionId: number;
  groupId: number;
  group: string;
  date: string;
  topic?: string | null;
  status: Status;
}

export interface AttendanceSummary {
  present: number;
  absent: number;
  recent: AttendanceEntry[];
}

export interface Member extends MemberRow {
  birthDate?: string | null;
  fideId?: string | null;
  nationalId?: string | null;
  notes?: string | null;
  photoConsent?: boolean | null;
  linkedUsername?: string | null;
  linkCode?: string | null;
  linkCodeExpires?: string | null;
  createdAt: string;
  updatedAt: string;
  noteEntries: Note[];
  attendance: AttendanceSummary;
  canDelete: boolean;
}

export interface MemberInput {
  firstName: string;
  lastName: string;
  birthDate: string | null;
  birthYear: number | null;
  level: string | null;
  fideId: string | null;
  nationalId: string | null;
  notes: string | null;
  photoConsent: boolean | null;
  archived: boolean;
  contacts: Contact[];
  groupIds: number[];
}

export interface LinkCode {
  code: string;
  expires: string;
}

export interface LinkState {
  linked: boolean;
  firstName?: string | null;
}

export interface Trainer {
  userId: number;
  username: string;
}

export interface GroupRow {
  id: number;
  name: string;
  schedule?: string | null;
  /** Trainingstag, 1 = Montag … 7 = Sonntag. */
  weekday?: number | null;
  archived: boolean;
  memberCount: number;
  sessionCount: number;
  lastSession?: string | null;
  trainers: Trainer[];
}

export interface Session {
  id: number;
  date: string;
  topic?: string | null;
  /** Was gemacht wurde, ausführlicher als das Thema. */
  notes?: string | null;
  present: number;
  absent: number;
}

export interface GroupMemberRow {
  id: number;
  firstName: string;
  lastName: string;
  birthYear?: number | null;
  level?: string | null;
  /** Status je Einheit in der Reihenfolge von `Group.sessions`; `null` = nicht erfasst. */
  statuses: (Status | null)[];
  present: number;
  recorded: number;
}

export interface Group extends GroupRow {
  sessions: Session[];
  members: GroupMemberRow[];
  canManage: boolean;
}

export interface GroupInput {
  name: string;
  schedule: string | null;
  weekday: number | null;
  archived: boolean;
}

export interface AttendanceInput {
  memberId: number;
  status: Status | null;
}

export interface SessionInput {
  date: string;
  topic: string | null;
  notes: string | null;
  attendance: AttendanceInput[];
}

export interface SessionDetail extends Session {
  groupId: number;
  attendance: AttendanceInput[];
}

export interface Progress {
  username: string;
  puzzleAttempts: number;
  puzzlesSolved: number;
  puzzleAccuracy: number;
  puzzleElo: number;
  bestStreak: number;
  minutes28: number;
  activeDays28: number;
  lastActive?: string | null;
  kidsLevelsDone: number;
  kidsStars: number;
  kidsCourseLines: number;
}
