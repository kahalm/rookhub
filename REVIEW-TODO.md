# Code Review Findings - RookHub + Crawler

Generated: 2026-05-28 (Updated after v0.19.3 fixes)

## Status Summary

**Fixed**: 40+ findings already addressed in v0.18.5 through v0.19.3
**Remaining**: Items below are either low-priority, architectural suggestions, or require significant refactoring

---

## Remaining Items (sorted by value)

### Medium Priority — Worth doing when time allows

#### M-6: No retry logic for failed API calls in the frontend
- **File**: Various frontend components
- **Issue**: API calls use single `catchError` with snackbar. No retry for transient 502/503.
- **Fix**: Consider a retry interceptor for 502/503/0 with exponential backoff, or manual retry buttons.

#### M-1: Tournament detail component is monolithic (900+ lines)
- **File**: `src/frontend/app/src/app/features/tournaments/tournament-detail.component.ts`
- **Fix**: Extract `TeamPlayersDialogComponent` into own file. Split into sub-components.

#### M-2: Endless puzzle component manages complex state in single component
- **File**: `src/frontend/app/src/app/features/puzzles/endless-puzzle.component.ts`
- **Fix**: Extract game state management into a dedicated service.

#### M-1 Crawler: HtmlParserService not covered by tests
- **File**: `src/ChessResultsCrawler/Services/HtmlParserService.cs`
- **Fix**: Add unit tests with sample HTML fixtures for each parser method.

#### M-3 Crawler: No timeout/cancellation on crawl operations
- **File**: `src/ChessResultsCrawler/Services/CrawlerService.cs`
- **Fix**: Thread `CancellationToken` through all crawl methods.

#### M-4 Crawler: RoundDetectionService hits chess-results.com on every check
- **File**: `src/ChessResultsCrawler/Services/RoundDetectionService.cs`
- **Fix**: Add short-lived cache (60s TTL) for round check results per tournament.

### Low Priority — Nice to have

#### L-10/L-12: No environment.prod.ts file — build relies on sed in Dockerfile
- **File**: `src/frontend/Dockerfile`, `src/frontend/app/src/environments/`
- **Fix**: Create `environment.prod.ts` and use Angular's `fileReplacements` in `angular.json`.

#### L-1 Tests: UnitTest1.cs contains 4 test classes in one file
- **File**: `tests/RookHub.Api.Tests/UnitTest1.cs`
- **Fix**: Split into `AuthServiceTests.cs`, `ProfileServiceTests.cs`, etc.

#### L-2 Tests: No CI pipeline definition
- **Fix**: Add GitHub Actions workflow for `dotnet test` + `ng build`.

#### L-5 Frontend: No custom favicon or PWA manifest
- **Fix**: Add favicon.ico and optionally manifest.json.

### Documentation / Architecture — No code change needed

- H-3 Frontend: JWT trusted without signature verification on client (inherent to client-side JWT — backend validates)
- H-5 Frontend: No CSRF (JWT in localStorage, not cookies — inherently safe)
- H-4 Frontend: XSS in tournament names (Angular escapes by default)
- C-4 Tests: InMemory DB limitations (document; consider Testcontainers for integration tests)
- C-3 Tests: No integration tests for CrawlerProxyService (would need WebApplicationFactory + mock)
- C-5 Tests: No dedicated BackgroundTaskQueue tests
- C-6 Tests: AutoSubscriptionService test coverage (verify edge cases)
- H-5 Tests: NoOpTaskQueue drops work (create ImmediateTaskQueue for tests)
- H-4 Tests: No E2E tests for subscriptions, monitors, favorites
- L-7 API: AutoSubscriptionService singleton+hosted registration pattern (works, just unusual)

---

## Already Fixed (for reference)

### v0.19.3
- C-7: Crawler GetAllTournaments pagination (page/pageSize)
- M-6: LogRetentionService for both projects (30-day retention)
- Frontend tournament list handles paginated response

### v0.19.2
- E2E test German text fix

### v0.19.1
- Crawler outgoing request logging (CrawlRequestLog)

### v0.18.5 and earlier
- C-3 API: CrawlerProxyService error forwarding (CrawlerRequestException)
- C-5 API: Puzzle CSV import size limit (500 MB)
- C-6 API: PuzzleService GetStatsAsync uses CountAsync + Take(1000)
- C-8 API: AutoSubscriptionService race condition (DbUpdateException catch)
- H-3 API: JWT key minimum length enforced (32 bytes)
- H-4 API: AdminSeeder only re-hashes if password changed
- H-6 API: TournamentMonitor scoped per-user
- H-7 API: nginx HSTS + security headers
- H-8 API: Puzzle themes LIKE sanitized (SanitizeLikeInput)
- M-2 API: BackgroundTaskQueue DropOldest (not Wait)
- M-4 API: FriendService search OrderBy
- M-7 API: AdminController ClearPuzzles transaction
- M-9 API: PuzzleService ID range cached (MemoryCache 5min)
- M-10 API: TournamentSubscription MaxLength(300)
- L-6 API: Health endpoint checks DB connectivity
- C-3 Frontend: Dashboard typed interfaces (no `any`)
- C-5 Frontend: Register password validation matches API
- C-6 Frontend: localStorage try/catch error handling
- M-4 Frontend: Dashboard takeUntilDestroyed
- C-3 Crawler: CrawlController uses IBackgroundTaskQueue (not Task.Run)
- C-6 Crawler: RequestLoggingMiddleware uses task queue
- H-4 Crawler: VPN rotation uses separate HttpClient (IHttpClientFactory)
- H-6 Crawler: ResolveTournamentAsync handles non-numeric IDs
- L-2 Crawler: ApiKeyMiddleware timing-safe comparison
- H-5 Crawler: Concurrent crawl jobs bounded by queue capacity
