using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data.Configurations;
using RookHub.Api.Models;

namespace RookHub.Api.Data;

/// <summary>
/// Der Datenbank-Kontext. Die Fluent-Konfiguration steht je Domäne in <c>Data/Configurations/</c>
/// (<see cref="IEntityTypeConfiguration{TEntity}"/>, eingesammelt in <see cref="OnModelCreating"/>), der
/// Konstruktor samt Schutz neuer Bücher ohne <see cref="BookSource"/> in <c>AppDbContext.BookSource.cs</c>.
/// </summary>
public partial class AppDbContext : DbContext
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<PuzzleChallenge> PuzzleChallenges => Set<PuzzleChallenge>();
    public DbSet<FavoritePuzzle> FavoritePuzzles => Set<FavoritePuzzle>();
    public DbSet<Worksheet> Worksheets => Set<Worksheet>();
    public DbSet<WorksheetItem> WorksheetItems => Set<WorksheetItem>();
    public DbSet<RevengeNotification> RevengeNotifications => Set<RevengeNotification>();
    public DbSet<Repertoire> Repertoires => Set<Repertoire>();
    public DbSet<RepertoireFile> RepertoireFiles => Set<RepertoireFile>();
    public DbSet<TournamentSubscription> TournamentSubscriptions => Set<TournamentSubscription>();
    public DbSet<TournamentFavorite> TournamentFavorites => Set<TournamentFavorite>();
    public DbSet<TournamentFavoriteDismissal> TournamentFavoriteDismissals => Set<TournamentFavoriteDismissal>();
    public DbSet<TournamentUserSetting> TournamentUserSettings => Set<TournamentUserSetting>();
    public DbSet<TournamentMonitor> TournamentMonitors => Set<TournamentMonitor>();
    public DbSet<TournamentDirectoryEntry> TournamentDirectoryEntries => Set<TournamentDirectoryEntry>();
    public DbSet<TournamentSearchProfile> TournamentSearchProfiles => Set<TournamentSearchProfile>();
    public DbSet<TournamentDirectorySweep> TournamentDirectorySweeps => Set<TournamentDirectorySweep>();
    public DbSet<TournamentDirectoryVenue> TournamentDirectoryVenues => Set<TournamentDirectoryVenue>();
    public DbSet<TournamentDirectoryRound> TournamentDirectoryRounds => Set<TournamentDirectoryRound>();
    public DbSet<TournamentDirectorySource> TournamentDirectorySources => Set<TournamentDirectorySource>();
    public DbSet<UserViewState> UserViewStates => Set<UserViewState>();
    public DbSet<TournamentDirectoryIgnore> TournamentDirectoryIgnores => Set<TournamentDirectoryIgnore>();
    public DbSet<PlayerTournamentResult> PlayerTournamentResults => Set<PlayerTournamentResult>();
    public DbSet<PlayerHistorySync> PlayerHistorySyncs => Set<PlayerHistorySync>();
    public DbSet<TrackedPlayer> TrackedPlayers => Set<TrackedPlayer>();
    public DbSet<HistoryTournamentCrawl> HistoryTournamentCrawls => Set<HistoryTournamentCrawl>();
    public DbSet<TournamentTimeControl> TournamentTimeControls => Set<TournamentTimeControl>();
    public DbSet<GeoPlace> GeoPlaces => Set<GeoPlace>();
    public DbSet<Puzzle> Puzzles => Set<Puzzle>();
    public DbSet<PuzzleAttempt> PuzzleAttempts => Set<PuzzleAttempt>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PuzzleTag> PuzzleTags => Set<PuzzleTag>();
    /// <summary>Als „besonders einfach" markierte Lichess-Puzzles der Kinderseite (Stufen-Leiter).</summary>
    public DbSet<KidsPuzzle> KidsPuzzles => Set<KidsPuzzle>();
    // LeagueHub (TMM-Aufstellungs-Prognosen)
    public DbSet<LeagueTournament> LeagueTournaments => Set<LeagueTournament>();
    public DbSet<LeagueRound> LeagueRounds => Set<LeagueRound>();
    public DbSet<LeagueMatch> LeagueMatches => Set<LeagueMatch>();
    public DbSet<LeagueGame> LeagueGames => Set<LeagueGame>();
    public DbSet<LeaguePlayer> LeaguePlayers => Set<LeaguePlayer>();
    public DbSet<LeaguePlayerProfile> LeaguePlayerProfiles => Set<LeaguePlayerProfile>();
    public DbSet<LeagueOnlineAccount> LeagueOnlineAccounts => Set<LeagueOnlineAccount>();
    public DbSet<LeagueOnlineGame> LeagueOnlineGames => Set<LeagueOnlineGame>();
    public DbSet<LeagueAccountSuggestion> LeagueAccountSuggestions => Set<LeagueAccountSuggestion>();
    public DbSet<LeagueAccountScan> LeagueAccountScans => Set<LeagueAccountScan>();
    public DbSet<LeagueBroadcast> LeagueBroadcasts => Set<LeagueBroadcast>();
    public DbSet<LeagueScoutAccount> LeagueScoutAccounts => Set<LeagueScoutAccount>();
    public DbSet<LeagueSelfReport> LeagueSelfReports => Set<LeagueSelfReport>();
    public DbSet<LeagueShare> LeagueShares => Set<LeagueShare>();
    public DbSet<LeagueView> LeagueViews => Set<LeagueView>();
    public DbSet<LeagueClubGame> LeagueClubGames => Set<LeagueClubGame>();
    public DbSet<LeagueMegaPlayer> LeagueMegaPlayers => Set<LeagueMegaPlayer>();
    public DbSet<LeagueNameAlias> LeagueNameAliases => Set<LeagueNameAlias>();
    public DbSet<LeagueClubDraft> LeagueClubDrafts => Set<LeagueClubDraft>();
    public DbSet<LeagueBatchUpload> LeagueBatchUploads => Set<LeagueBatchUpload>();
    public DbSet<LeagueBatchUploadFile> LeagueBatchUploadFiles => Set<LeagueBatchUploadFile>();
    public DbSet<LeagueClub> LeagueClubs => Set<LeagueClub>();
    public DbSet<LeagueClubMember> LeagueClubMembers => Set<LeagueClubMember>();
    public DbSet<LeagueGameMove> LeagueGameMoves => Set<LeagueGameMove>();
    // Spielervorbereitung (Prep): Partiebestand aus Megabase + Lumbra
    public DbSet<PrepPlayer> PrepPlayers => Set<PrepPlayer>();
    public DbSet<PrepEvent> PrepEvents => Set<PrepEvent>();
    public DbSet<PrepGame> PrepGames => Set<PrepGame>();
    public DbSet<PrepImport> PrepImports => Set<PrepImport>();
    // ClubHub (Kartei der Kinder und Jugendlichen)
    public DbSet<ClubMember> ClubMembers => Set<ClubMember>();
    public DbSet<ClubMemberPhoto> ClubMemberPhotos => Set<ClubMemberPhoto>();
    public DbSet<ClubContact> ClubContacts => Set<ClubContact>();
    public DbSet<ClubGroup> ClubGroups => Set<ClubGroup>();
    public DbSet<ClubGroupMember> ClubGroupMembers => Set<ClubGroupMember>();
    public DbSet<ClubGroupTrainer> ClubGroupTrainers => Set<ClubGroupTrainer>();
    public DbSet<ClubSession> ClubSessions => Set<ClubSession>();
    public DbSet<ClubAttendance> ClubAttendances => Set<ClubAttendance>();
    public DbSet<ClubSessionPhoto> ClubSessionPhotos => Set<ClubSessionPhoto>();
    public DbSet<ClubNote> ClubNotes => Set<ClubNote>();
    /// <summary>KidHub-Fortschritt angemeldeter Kinder: Stufen, Kurse, geloeste Kurs-Linien.</summary>
    public DbSet<KidsLevelProgress> KidsLevelProgresses => Set<KidsLevelProgress>();
    public DbSet<KidsCourseProgress> KidsCourseProgresses => Set<KidsCourseProgress>();
    public DbSet<KidsCourseLine> KidsCourseLines => Set<KidsCourseLine>();
    public DbSet<BookPuzzle> BookPuzzles => Set<BookPuzzle>();
    public DbSet<BookPuzzleAttempt> BookPuzzleAttempts => Set<BookPuzzleAttempt>();
    public DbSet<SharedPuzzleAttempt> SharedPuzzleAttempts => Set<SharedPuzzleAttempt>();
    public DbSet<Book> Books => Set<Book>();
    /// <summary>Roh-PGN der Bücher — Tabellensplitting auf <c>Books</c> (siehe <see cref="BookSource"/>).</summary>
    public DbSet<BookSource> BookSources => Set<BookSource>();
    public DbSet<CatalogGrant> CatalogGrants => Set<CatalogGrant>();
    public DbSet<CatalogRequest> CatalogRequests => Set<CatalogRequest>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<GroupRole> GroupRoles => Set<GroupRole>();
    public DbSet<EndlessProgress> EndlessProgresses => Set<EndlessProgress>();
    public DbSet<EndlessSession> EndlessSessions => Set<EndlessSession>();
    public DbSet<CourseProgress> CourseProgresses => Set<CourseProgress>();
    public DbSet<CoursePuzzleResult> CoursePuzzleResults => Set<CoursePuzzleResult>();
    public DbSet<CoursePin> CoursePins => Set<CoursePin>();
    public DbSet<CourseShare> CourseShares => Set<CourseShare>();
    public DbSet<CourseLink> CourseLinks => Set<CourseLink>();
    public DbSet<CourseInfoView> CourseInfoViews => Set<CourseInfoView>();
    public DbSet<CourseLineReset> CourseLineResets => Set<CourseLineReset>();
    public DbSet<CourseAttempt> CourseAttempts => Set<CourseAttempt>();
    public DbSet<BookGroupAccess> BookGroupAccesses => Set<BookGroupAccess>();
    public DbSet<WeeklyPost> WeeklyPosts => Set<WeeklyPost>();
    public DbSet<WeeklyPostAttempt> WeeklyPostAttempts => Set<WeeklyPostAttempt>();
    public DbSet<DailyPuzzle> DailyPuzzles => Set<DailyPuzzle>();
    public DbSet<UserApiToken> UserApiTokens => Set<UserApiToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AuthHandoffToken> AuthHandoffTokens => Set<AuthHandoffToken>();
    public DbSet<GroupTrainingGoal> GroupTrainingGoals => Set<GroupTrainingGoal>();
    public DbSet<UserTrainingGoal> UserTrainingGoals => Set<UserTrainingGoal>();
    public DbSet<PlayTimeDaily> PlayTimeDailies => Set<PlayTimeDaily>();
    public DbSet<PlayTimeSync> PlayTimeSyncs => Set<PlayTimeSync>();
    public DbSet<ChessableCredential> ChessableCredentials => Set<ChessableCredential>();
    public DbSet<LichessEngineCredential> LichessEngineCredentials => Set<LichessEngineCredential>();
    public DbSet<ExternalEngineRegistration> ExternalEngineRegistrations => Set<ExternalEngineRegistration>();
    public DbSet<EngineClientSchedule> EngineClientSchedules => Set<EngineClientSchedule>();
    public DbSet<LichessExplorerCacheEntry> LichessExplorerCacheEntries => Set<LichessExplorerCacheEntry>();
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<AnalysisHistoryEntry> AnalysisHistoryEntries => Set<AnalysisHistoryEntry>();
    public DbSet<MoveComparison> MoveComparisons => Set<MoveComparison>();
    public DbSet<MoveComparisonLine> MoveComparisonLines => Set<MoveComparisonLine>();
    public DbSet<LibraryGame> LibraryGames => Set<LibraryGame>();
    public DbSet<CommentSet> CommentSets => Set<CommentSet>();
    public DbSet<CommentText> CommentTexts => Set<CommentText>();
    public DbSet<CourseTranslationJob> CourseTranslationJobs => Set<CourseTranslationJob>();
    public DbSet<GameAnalysis> GameAnalyses => Set<GameAnalysis>();
    public DbSet<GameAnalysisPosition> GameAnalysisPositions => Set<GameAnalysisPosition>();
    public DbSet<GameMoveExplanation> GameMoveExplanations => Set<GameMoveExplanation>();
    public DbSet<GameRoast> GameRoasts => Set<GameRoast>();
    public DbSet<GameRecap> GameRecaps => Set<GameRecap>();
    public DbSet<CommentEmbedding> CommentEmbeddings => Set<CommentEmbedding>();
    public DbSet<GuessSession> GuessSessions => Set<GuessSession>();
    public DbSet<GuessMove> GuessMoves => Set<GuessMove>();
    public DbSet<ChessableImport> ChessableImports => Set<ChessableImport>();
    public DbSet<MenuItemSetting> MenuItemSettings => Set<MenuItemSetting>();
    public DbSet<MenuItemGroupAccess> MenuItemGroupAccesses => Set<MenuItemGroupAccess>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AdminMessage> AdminMessages => Set<AdminMessage>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<ChessableActivity> ChessableActivities => Set<ChessableActivity>();
    public DbSet<ChessableCourseTheme> ChessableCourseThemes => Set<ChessableCourseTheme>();
    public DbSet<ChessableProblemMove> ChessableProblemMoves => Set<ChessableProblemMove>();
    public DbSet<ChessableReviewLine> ChessableReviewLines => Set<ChessableReviewLine>();
    public DbSet<AnonymousChessableReviewLine> AnonymousChessableReviewLines => Set<AnonymousChessableReviewLine>();
    public DbSet<ChessableSessionMove> ChessableSessionMoves => Set<ChessableSessionMove>();
    public DbSet<CourseFlashcardMark> CourseFlashcardMarks => Set<CourseFlashcardMark>();
    public DbSet<RepertoireFlashcardMark> RepertoireFlashcardMarks => Set<RepertoireFlashcardMark>();
    public DbSet<ManualActivity> ManualActivities => Set<ManualActivity>();
    public DbSet<ActivityPreset> ActivityPresets => Set<ActivityPreset>();
    public DbSet<ActivityTimer> ActivityTimers => Set<ActivityTimer>();
    public DbSet<RememberedPosition> RememberedPositions => Set<RememberedPosition>();
    public DbSet<CiBuildReport> CiBuildReports => Set<CiBuildReport>();
    public DbSet<SavedGame> SavedGames => Set<SavedGame>();
    public DbSet<ScoresheetScan> ScoresheetScans => Set<ScoresheetScan>();
    public DbSet<ScoresheetScanArchive> ScoresheetScanArchives => Set<ScoresheetScanArchive>();
    public DbSet<TacticCandidate> TacticCandidates => Set<TacticCandidate>();
    public DbSet<ScoresheetScanPage> ScoresheetScanPages => Set<ScoresheetScanPage>();
    public DbSet<ScoresheetScanView> ScoresheetScanViews => Set<ScoresheetScanView>();
    public DbSet<GameMistakeProgress> GameMistakeProgresses => Set<GameMistakeProgress>();
    public DbSet<SharedLine> SharedLines => Set<SharedLine>();
    public DbSet<GameReconstruction> GameReconstructions => Set<GameReconstruction>();
    public DbSet<GameReconstructionPart> GameReconstructionParts => Set<GameReconstructionPart>();

    public DbSet<RepertoireCardState> RepertoireCardStates => Set<RepertoireCardState>();
    public DbSet<RepertoireSrSettings> RepertoireSrSettings => Set<RepertoireSrSettings>();
    public DbSet<RepertoireShare> RepertoireShares => Set<RepertoireShare>();
    public DbSet<UserPushSubscription> UserPushSubscriptions => Set<UserPushSubscription>();
    public DbSet<NotificationPushSetting> NotificationPushSettings => Set<NotificationPushSetting>();
    public DbSet<CalculationTree> CalculationTrees => Set<CalculationTree>();
    public DbSet<CalcEdition> CalcEditions => Set<CalcEdition>();
    public DbSet<CalcSeriesMember> CalcSeriesMembers => Set<CalcSeriesMember>();
    public DbSet<CalcEditionView> CalcEditionViews => Set<CalcEditionView>();

    /// <summary>Jede Zeitspalte liest sich als UTC zurück (siehe <see cref="UtcDateTimeConverter"/>) — sonst ginge
    /// jeder gelesene Wert ohne „Z" über die Leitung und der Browser nähme ihn als Ortszeit.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    /// <summary>Sammelt jede <see cref="IEntityTypeConfiguration{TEntity}"/> aus <c>Data/Configurations/</c> ein —
    /// eine neue Entität bekommt ihre Konfiguration dort, in der Datei ihrer Domäne.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly,
            t => t.Namespace == typeof(AppUserConfiguration).Namespace);
}
