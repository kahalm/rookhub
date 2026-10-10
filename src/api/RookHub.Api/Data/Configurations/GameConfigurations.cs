using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Partien und Analyse: gespeicherte Partien, Formular-Einlesung, Rekonstruktion, Engines, Meisterpartien, Analyse-Texte, Zugvergleich.

internal sealed class GameMistakeProgressConfiguration : IEntityTypeConfiguration<GameMistakeProgress>
{
    public void Configure(EntityTypeBuilder<GameMistakeProgress> e)
    {
        e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(p => p.Game).WithMany().HasForeignKey(p => p.SavedGameId).OnDelete(DeleteBehavior.Cascade);
        e.Property(p => p.SolvedPlies).HasMaxLength(4000);
        e.Property(p => p.DismissedPlies).HasMaxLength(4000);
        e.HasIndex(p => new { p.UserId, p.SavedGameId }).IsUnique();
    }
}

internal sealed class SavedGameConfiguration : IEntityTypeConfiguration<SavedGame>
{
    public void Configure(EntityTypeBuilder<SavedGame> e)
    {
        e.HasOne(g => g.User)
         .WithMany()
         .HasForeignKey(g => g.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(g => g.Source).HasMaxLength(20);
        e.Property(g => g.ExternalId).HasMaxLength(120);
        e.Property(g => g.Pgn).HasColumnType("LONGTEXT");
        e.Property(g => g.White).HasMaxLength(120);
        e.Property(g => g.Black).HasMaxLength(120);
        e.Property(g => g.Result).HasMaxLength(12);
        e.Property(g => g.SourceUrl).HasMaxLength(1000);
        e.Property(g => g.ReviewLanguage).HasMaxLength(8);
        e.Property(g => g.ShareToken).HasMaxLength(32);
        e.Property(g => g.TimeControl).HasMaxLength(32);
        e.Property(g => g.OwnerSide).HasMaxLength(5);
        e.Property(g => g.Classifier1).HasMaxLength(80);
        e.Property(g => g.Classifier2).HasMaxLength(80);
        e.Property(g => g.Tags).HasMaxLength(Services.GameTags.MaxStoredLength);
        e.HasIndex(g => g.ShareToken).IsUnique();
        // Auflistung je User (neueste zuerst).
        e.HasIndex(g => new { g.UserId, g.CreatedAt });
        // Dedup hart auf DB-Ebene erzwingen (statt nur check-then-insert): dieselbe externe Partie kann
        // nicht doppelt gespeichert werden, auch nicht bei parallelem Doppel-Klick. MySQL behandelt
        // NULL-ExternalId als verschieden → mehrere Saves OHNE externe Id (manuell) bleiben erlaubt.
        e.HasIndex(g => new { g.UserId, g.Source, g.ExternalId }).IsUnique();
        e.HasIndex(g => g.LeagueClubGameId);   // Kopien einer Vereinspartie (0.660.0)
    }
}

internal sealed class TacticCandidateConfiguration : IEntityTypeConfiguration<TacticCandidate>
{
    public void Configure(EntityTypeBuilder<TacticCandidate> e)
    {
        e.HasOne(t => t.GameAnalysis).WithMany().HasForeignKey(t => t.GameAnalysisId).OnDelete(DeleteBehavior.Cascade);
        e.Property(t => t.PrevFen).HasMaxLength(120);
        e.Property(t => t.Fen).HasMaxLength(120);
        e.Property(t => t.NextFen).HasMaxLength(120);
        e.Property(t => t.BlunderUci).HasMaxLength(10);
        e.Property(t => t.GameMoveUci).HasMaxLength(10);
        e.Property(t => t.PendingReplyUci).HasMaxLength(10);
        e.Property(t => t.Kind).HasMaxLength(16);
        e.Property(t => t.Moves).HasMaxLength(300);
        e.Property(t => t.RejectReason).HasMaxLength(40);
        e.Property(t => t.Themes).HasMaxLength(200);
        e.Property(t => t.EvalText).HasMaxLength(16);
        e.Property(t => t.LineId).HasMaxLength(300);
        e.Property(t => t.SecondBest).HasMaxLength(10);
        e.Property(t => t.SecondEval).HasMaxLength(16);
        e.Property(t => t.SecondHereJson).HasMaxLength(2000);
        e.HasIndex(t => new { t.GameAnalysisId, t.Ply }).IsUnique();
        e.HasIndex(t => new { t.Status, t.UpdatedAt });
        e.HasIndex(t => t.AnalysisJobId);
        e.HasIndex(t => t.SecondJobId);
    }
}

internal sealed class ScoresheetScanArchiveConfiguration : IEntityTypeConfiguration<ScoresheetScanArchive>
{
    public void Configure(EntityTypeBuilder<ScoresheetScanArchive> e)
    {
        e.HasOne(a => a.Scan).WithMany().HasForeignKey(a => a.ScoresheetScanId).OnDelete(DeleteBehavior.Cascade);
        e.Property(a => a.Photo).HasColumnType("LONGBLOB");
        e.Property(a => a.ContentType).HasMaxLength(40);
        e.Property(a => a.TranscriptionJson).HasColumnType("LONGTEXT");
        e.Property(a => a.ResolutionJson).HasColumnType("LONGTEXT");
        e.Property(a => a.FinalPgn).HasColumnType("LONGTEXT");
        e.Property(a => a.Outcome).HasMaxLength(16);
        e.Property(a => a.Model).HasMaxLength(60);
        e.Property(a => a.NotationLanguage).HasMaxLength(8);
        e.HasIndex(a => a.ExpiresAt);
        e.HasIndex(a => new { a.ScoresheetScanId, a.Page }).IsUnique();
        e.HasIndex(a => a.LeagueClubGameId);
    }
}

internal sealed class ScoresheetScanConfiguration : IEntityTypeConfiguration<ScoresheetScan>
{
    public void Configure(EntityTypeBuilder<ScoresheetScan> e)
    {
        e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        // Das Foto gehoert zur Partie: wird sie geloescht, geht es mit.
        e.HasOne(s => s.Game).WithMany().HasForeignKey(s => s.SavedGameId).OnDelete(DeleteBehavior.Cascade);
        e.Property(s => s.Photo).HasColumnType("LONGBLOB");
        e.Property(s => s.Purpose).HasMaxLength(16);
        e.Property(s => s.AccessKey).HasMaxLength(32);
        e.Property(s => s.AnonIpHash).HasMaxLength(64);
        e.HasIndex(s => s.AccessKey).IsUnique();
        e.HasIndex(s => new { s.AnonIpHash, s.CreatedAt });
        e.Property(s => s.ContentType).HasMaxLength(40);
        e.Property(s => s.FileName).HasMaxLength(200);
        e.Property(s => s.NotationLanguage).HasMaxLength(8);
        e.Property(s => s.OwnerSide).HasMaxLength(8);
        e.Property(s => s.Error).HasMaxLength(40);
        e.Property(s => s.Model).HasMaxLength(60);
        e.Property(s => s.TranscriptionJson).HasColumnType("LONGTEXT");
        e.Property(s => s.ResolutionJson).HasColumnType("LONGTEXT");
        // Der Worker sucht die naechste wartende Einlesung; die Tagesgrenze zaehlt je User.
        e.HasIndex(s => new { s.Status, s.CreatedAt });
        e.HasIndex(s => new { s.UserId, s.CreatedAt });
        e.HasIndex(s => s.SavedGameId);
        // Liga-Einlesungen gehören einem Verein (Mandanten-Schritt 2026-10-07); RookHubs eigene haben keinen.
        e.HasIndex(s => s.ClubId);
        e.HasOne<LeagueClub>().WithMany().HasForeignKey(s => s.ClubId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScoresheetScanPageConfiguration : IEntityTypeConfiguration<ScoresheetScanPage>
{
    public void Configure(EntityTypeBuilder<ScoresheetScanPage> e)
    {
        e.HasOne(p => p.Scan).WithMany(s => s.Pages).HasForeignKey(p => p.ScoresheetScanId).OnDelete(DeleteBehavior.Cascade);
        e.Property(p => p.Photo).HasColumnType("LONGBLOB");
        e.Property(p => p.ContentType).HasMaxLength(40);
        e.Property(p => p.FileName).HasMaxLength(200);
        e.HasIndex(p => new { p.ScoresheetScanId, p.Page }).IsUnique();
    }
}

internal sealed class ScoresheetScanViewConfiguration : IEntityTypeConfiguration<ScoresheetScanView>
{
    public void Configure(EntityTypeBuilder<ScoresheetScanView> e)
    {
        e.HasOne(v => v.Scan).WithMany(s => s.Views).HasForeignKey(v => v.ScoresheetScanId).OnDelete(DeleteBehavior.Cascade);
        e.Property(v => v.Photo).HasColumnType("LONGBLOB");
        e.Property(v => v.ContentType).HasMaxLength(40);
        e.Property(v => v.FileName).HasMaxLength(200);
        e.HasIndex(v => new { v.ScoresheetScanId, v.Page, v.View }).IsUnique();
    }
}

internal sealed class GameReconstructionConfiguration : IEntityTypeConfiguration<GameReconstruction>
{
    public void Configure(EntityTypeBuilder<GameReconstruction> e)
    {
        e.HasOne(r => r.User)
         .WithMany()
         .HasForeignKey(r => r.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(r => r.Title).HasMaxLength(200);
        e.Property(r => r.White).HasMaxLength(120);
        e.Property(r => r.Black).HasMaxLength(120);
        e.Property(r => r.Event).HasMaxLength(200);
        e.Property(r => r.Result).HasMaxLength(12);
        e.Property(r => r.Note).HasMaxLength(2000);
        e.Property(r => r.ShareToken).HasMaxLength(32);
        // Liste je Nutzer, zuletzt geaendert zuerst.
        e.HasIndex(r => new { r.UserId, r.UpdatedAt });
        // Einstieg des oeffentlichen Links (/r/{token}); NULL = nicht geteilt (MariaDB laesst
        // beliebig viele NULLs im Unique-Index zu).
        e.HasIndex(r => r.ShareToken).IsUnique();
    }
}

internal sealed class GameReconstructionPartConfiguration : IEntityTypeConfiguration<GameReconstructionPart>
{
    public void Configure(EntityTypeBuilder<GameReconstructionPart> e)
    {
        e.HasOne(p => p.Reconstruction)
         .WithMany(r => r.Parts)
         .HasForeignKey(p => p.GameReconstructionId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(p => p.Moves).HasMaxLength(4000);
        e.Property(p => p.Fen).HasMaxLength(120);
        e.Property(p => p.Note).HasMaxLength(500);
        // Die Teile werden IMMER in ihrer Reihenfolge gelesen.
        e.HasIndex(p => new { p.GameReconstructionId, p.Ordinal });
    }
}

internal sealed class LichessEngineCredentialConfiguration : IEntityTypeConfiguration<LichessEngineCredential>
{
    public void Configure(EntityTypeBuilder<LichessEngineCredential> e)
    {
        // 1:1 zu AppUser; Cascade-Delete entfernt den verschluesselten Lichess-Token mit dem User.
        e.HasIndex(c => c.UserId).IsUnique();
        e.HasOne(c => c.User)
         .WithMany()
         .HasForeignKey(c => c.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(c => c.EncryptedToken).HasColumnType("TEXT");
    }
}

internal sealed class ExternalEngineRegistrationConfiguration : IEntityTypeConfiguration<ExternalEngineRegistration>
{
    public void Configure(EntityTypeBuilder<ExternalEngineRegistration> e)
    {
        // Der Name ist die Identitaet einer Registrierung (der Provider aktualisiert per Name).
        e.HasIndex(r => new { r.UserId, r.Name }).IsUnique();
        // Der Broker findet die Engine eines Pollers ueber den Selector — nicht eindeutig: ein fest
        // gesetztes PROVIDER_SECRET darf mehrere Registrierungen bedienen (wie bei lila-engine).
        e.HasIndex(r => r.ProviderSelector);
        e.HasOne(r => r.User)
         .WithMany()
         .HasForeignKey(r => r.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EngineClientScheduleConfiguration : IEntityTypeConfiguration<EngineClientSchedule>
{
    public void Configure(EntityTypeBuilder<EngineClientSchedule> e)
    {
        // Je Konto und Engine-NAME genau ein Zeitplan — derselbe Schluessel wie bei der Registrierung.
        e.HasIndex(s => new { s.UserId, s.EngineName }).IsUnique();
        e.HasOne<AppUser>()
         .WithMany()
         .HasForeignKey(s => s.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LichessExplorerCacheEntryConfiguration : IEntityTypeConfiguration<LichessExplorerCacheEntry>
{
    public void Configure(EntityTypeBuilder<LichessExplorerCacheEntry> e)
    {
        // Nachschlagen immer über den Schlüssel; der Unique-Index fängt zwei Läufe ab, die
        // dieselbe Stellung gleichzeitig abrufen (DbIdempotency).
        e.HasIndex(c => c.CacheKey).IsUnique();
        e.Property(c => c.Json).HasColumnType("TEXT");
    }
}

internal sealed class LibraryGameConfiguration : IEntityTypeConfiguration<LibraryGame>
{
    public void Configure(EntityTypeBuilder<LibraryGame> e)
    {
        // „Kenne ich die Zuege schon?" — die Frage beim Einlesen, 130 000-mal je Sammlung.
        e.HasIndex(g => g.MovesHash);
        // Die Arbeitsliste der Vorsortierung: erst der Zustand, dann die Note.
        e.HasIndex(g => new { g.Status, g.Score });
        // Die Bestandssuche OHNE Suchbegriff sortiert nach der Note ueber den ganzen Bestand.
        // Der zusammengesetzte Index oben hilft dabei nicht: sein fuehrendes Feld ist der
        // Zustand, und „Zustand ist nicht Dublette/aussortiert" ist keine Bereichssuche, die
        // sortiert herauskommt. Gemessen (130 572 Zeilen): erste Seite 13,6 s ohne diesen
        // Index, 3 ms mit ihm.
        e.HasIndex(g => g.Score);
        // „Alles von Aagaard" bzw. „alles aus CBM 104" — die zwei Griffe der Durchsicht.
        e.HasIndex(g => g.Annotator);
        e.HasIndex(g => g.SourceTitle);
        // „Wie viele Partien spielen dieselben ersten k Zuege?" — eine Praefix-Suche (LIKE 'e4 e5%'),
        // und die nutzt den Index, solange der Platzhalter hinten steht.
        e.HasIndex(g => g.OpeningLine);
        // Derselbe Praefix, aber ZAEHLEND (GuessOpeningTree) — und dort muss der Index die
        // Abfrage ABDECKEN. Mit dem Praefix allein waehlt MariaDB bei „e4 %" (die halbe Tabelle)
        // den vollen Tabellenscan, und der laeuft ueber die LONGTEXT-Spalte mit den PGNs:
        // gemessen 22,4 s gegen 0,25 s. Status gehoert deshalb VOR die Zeile.
        e.HasIndex(g => new { g.Status, g.OpeningLine });
        // Der erste grobe Filter (Partien einer brauchbaren Laenge mit genug Kommentaren).
        e.HasIndex(g => new { g.CommentedPlies, g.PlyCount });
        // Selbstbezug: die Dublette zeigt auf die zuerst eingelesene Fassung. Kein Cascade —
        // faellt das Original weg, soll die zweite Fassung bleiben und nicht mitgerissen werden.
        e.HasOne<LibraryGame>()
         .WithMany()
         .HasForeignKey(g => g.DuplicateOfId)
         .OnDelete(DeleteBehavior.Restrict);
        // Uebernommene Partie: bewusst OHNE Fremdschluessel. Wird die Analyse geloescht, soll
        // die Bibliothekszeile stehen bleiben — sie ist der Bestand, nicht die Rechnung.
        e.Property(g => g.Pgn).HasColumnType("LONGTEXT");
    }
}

internal sealed class GameAnalysisConfiguration : IEntityTypeConfiguration<GameAnalysis>
{
    public void Configure(EntityTypeBuilder<GameAnalysis> e)
    {
        e.HasIndex(g => new { g.UserId, g.CreatedAt });
        e.HasIndex(g => g.Status);   // die Pumpe holt sich die unfertigen
        // „Habe ich diese Bibliothekspartie schon angefordert?" — die Frage stellt die
        // Bestandssuche fuer JEDE angezeigte Zeile.
        e.HasIndex(g => new { g.UserId, g.LibraryGameId });
        // „Hat diese Vereinspartie schon eine Analyse?" — der Takt des Stapels fragt es fuer jede Partie.
        e.HasIndex(g => g.LeagueClubGameId);
        // Der Stellungsfilter sucht ueber ein PRAEFIX („e4 e5 Nf3%") — und das nutzt den
        // Index, solange der Platzhalter hinten steht.
        e.Property(g => g.OpeningLine).HasMaxLength(200);
        e.HasIndex(g => g.OpeningLine);
        // Abdeckend fuer die ZAEHLUNG des Baums — dieselbe Ueberlegung wie bei LibraryGames.
        e.HasIndex(g => new { g.IsPublic, g.OpeningLine });
        e.HasOne(g => g.User)
         .WithMany()
         .HasForeignKey(g => g.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(g => g.Pgn).HasColumnType("LONGTEXT");
        e.Property(g => g.MovesHash).HasMaxLength(64);
        e.HasIndex(g => g.MovesHash);   // Liga-Partien des Stapels (0.665.0)
    }
}

// Frag die Kommentare (0.536.0): Vektor als MariaDB VECTOR(512); der Kosinus-Index kommt per SQL aus der Migration.
internal sealed class CommentEmbeddingConfiguration : IEntityTypeConfiguration<CommentEmbedding>
{
    public void Configure(EntityTypeBuilder<CommentEmbedding> e)
    {
        e.Property(x => x.Vector).HasColumnType($"vector({CommentEmbedding.Dimensions})");

        e.HasIndex(x => x.LibraryGameId);

        e.HasOne(x => x.LibraryGame).WithMany().HasForeignKey(x => x.LibraryGameId).OnDelete(DeleteBehavior.Cascade);
    }
}

// Roast my game (0.535.0): je Partie, Sprache und Stil der zuletzt gewuerfelte Text; geht mit der Partie.
internal sealed class GameRoastConfiguration : IEntityTypeConfiguration<GameRoast>
{
    public void Configure(EntityTypeBuilder<GameRoast> e)
    {
        e.HasIndex(x => new { x.SavedGameId, x.Language, x.Style }).IsUnique();

        e.HasOne(x => x.SavedGame).WithMany().HasForeignKey(x => x.SavedGameId).OnDelete(DeleteBehavior.Cascade);
    }
}

// Kurz erzaehlt (0.541.0): je Partie und Sprache EIN Text fuer Link-Vorschau + Partieseite; geht mit der Partie.
internal sealed class GameRecapConfiguration : IEntityTypeConfiguration<GameRecap>
{
    public void Configure(EntityTypeBuilder<GameRecap> e)
    {
        e.HasIndex(x => new { x.SavedGameId, x.Language }).IsUnique();

        e.HasOne(x => x.SavedGame).WithMany().HasForeignKey(x => x.SavedGameId).OnDelete(DeleteBehavior.Cascade);
    }
}

// Warum war das ein Fehler? (0.534.0): je Analyse, Halbzug und Sprache EIN Text; geht mit der Analyse.
internal sealed class GameMoveExplanationConfiguration : IEntityTypeConfiguration<GameMoveExplanation>
{
    public void Configure(EntityTypeBuilder<GameMoveExplanation> e)
    {
        e.HasIndex(x => new { x.GameAnalysisId, x.Ply, x.Language }).IsUnique();

        e.HasOne(x => x.GameAnalysis).WithMany().HasForeignKey(x => x.GameAnalysisId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GameAnalysisPositionConfiguration : IEntityTypeConfiguration<GameAnalysisPosition>
{
    public void Configure(EntityTypeBuilder<GameAnalysisPosition> e)
    {
        // Je Partie gibt es jeden Halbzug genau einmal.
        e.HasIndex(p => new { p.GameAnalysisId, p.Ply }).IsUnique();
        e.HasOne(p => p.GameAnalysis)
         .WithMany(g => g.Positions)
         .HasForeignKey(p => p.GameAnalysisId)
         .OnDelete(DeleteBehavior.Cascade);
        // Kandidatenliste: klein je Zeile, aber 5 Varianten mit Zug+Bewertung je Halbzug summieren sich.
        e.Property(p => p.CandidatesJson).HasColumnType("LONGTEXT");
    }
}

internal sealed class GuessSessionConfiguration : IEntityTypeConfiguration<GuessSession>
{
    public void Configure(EntityTypeBuilder<GuessSession> e)
    {
        e.HasIndex(x => new { x.UserId, x.StartedAt });
        // Der anonyme Besitzer wird genauso abgefragt wie der angemeldete
        // (GuessSessionService.OwnedBy) und braucht deshalb denselben Index.
        e.HasIndex(x => new { x.AnonymousSessionId, x.StartedAt });
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // Restrict statt Cascade: sonst gäbe es zwei Cascade-Pfade auf GuessMoves
        // (User → Session → Move und Analyse → Session → Move) — MySQL lehnt das ab.
        e.HasOne(x => x.GameAnalysis).WithMany().HasForeignKey(x => x.GameAnalysisId)
         .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GuessMoveConfiguration : IEntityTypeConfiguration<GuessMove>
{
    public void Configure(EntityTypeBuilder<GuessMove> e)
    {
        e.HasIndex(x => new { x.GuessSessionId, x.Ply }).IsUnique();
        e.HasOne(x => x.GuessSession).WithMany(s => s.Moves).HasForeignKey(x => x.GuessSessionId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AnalysisJobConfiguration : IEntityTypeConfiguration<AnalysisJob>
{
    public void Configure(EntityTypeBuilder<AnalysisJob> e)
    {
        // Worker fragt je User nach laufbereiten Aufträgen; Liste sortiert nach Anlage.
        e.HasIndex(j => new { j.UserId, j.Status });
        e.HasIndex(j => new { j.UserId, j.CreatedAt });
        e.HasOne(j => j.User)
         .WithMany()
         .HasForeignKey(j => j.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(j => j.ResultJson).HasColumnType("LONGTEXT");
        e.Property(j => j.EvalText).HasMaxLength(16);
    }
}

// Analyse-Verlauf (0.603.0): die letzten Analysen des Analysebretts je Nutzer.
internal sealed class AnalysisHistoryEntryConfiguration : IEntityTypeConfiguration<AnalysisHistoryEntry>
{
    public void Configure(EntityTypeBuilder<AnalysisHistoryEntry> e)
    {
        e.HasIndex(h => new { h.UserId, h.UpdatedAt });
        e.HasOne(h => h.User)
         .WithMany()
         .HasForeignKey(h => h.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(h => h.Moves).HasColumnType("TEXT");
        e.Property(h => h.TreeJson).HasColumnType("LONGTEXT");   // Zugbaum samt Varianten (0.604.0)
    }
}

// Züge vergleichen (0.602.0): Kopf je Vergleich, eine Zeile je gerechneter Stellung.
internal sealed class MoveComparisonConfiguration : IEntityTypeConfiguration<MoveComparison>
{
    public void Configure(EntityTypeBuilder<MoveComparison> e)
    {
        e.HasIndex(c => new { c.UserId, c.CreatedAt });
        e.HasIndex(c => c.Status);
        e.HasOne(c => c.User)
         .WithMany()
         .HasForeignKey(c => c.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MoveComparisonLineConfiguration : IEntityTypeConfiguration<MoveComparisonLine>
{
    public void Configure(EntityTypeBuilder<MoveComparisonLine> e)
    {
        e.HasIndex(l => l.MoveComparisonId);
        // Die Auftragsliste blendet Aufträge eines Vergleichs aus (Unterabfrage je Auftrag).
        e.HasIndex(l => l.AnalysisJobId);
        e.HasOne(l => l.Comparison)
         .WithMany(c => c.Lines)
         .HasForeignKey(l => l.MoveComparisonId)
         .OnDelete(DeleteBehavior.Cascade);
        e.Property(l => l.ResultJson).HasColumnType("LONGTEXT");
    }
}
