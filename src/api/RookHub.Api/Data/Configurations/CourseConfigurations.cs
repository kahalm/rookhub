using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Bücher und Kurse: Buch samt Roh-PGN, Kurs-Puzzles, Fortschritt, Freigaben, Übersetzungen.

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> e)
    {
        e.HasIndex(b => b.FileName).IsUnique();
        e.HasIndex(b => b.OwnerUserId);
        // Öffentlicher Kurz-Alias eindeutig (mehrere NULLs erlaubt: MySQL wertet NULL im
        // Unique-Index nicht als gleich → Bücher ohne Alias kollidieren nicht).
        e.HasIndex(b => b.PublicSlug).IsUnique();

        // Roh-PGN per TABELLENSPLITTING: BookSource liegt in derselben Zeile (Spalte SourcePgn),
        // ist aber eine eigene Entität — ein Include(bp => bp.Book) lädt den Text damit nicht mehr
        // mit (vorher 11 GB aus der DB für EINEN Kurs-Puzzle-Request). Kein Schemaeingriff.
        // Pflicht-Navigation: relational liefert Include damit IMMER eine Instanz (auch bei
        // SourcePgn NULL; InMemory: null, wenn die BookSource-Zeile fehlt). Beim Anlegen verlangt EF
        // KEINE BookSource (relational: INSERT ohne die Spalte → NULL) — das erzwingt erst
        // RequireSourceOnNewBook (AppDbContext.BookSource.cs). Regeln/Fallen: siehe BookSource.
        e.HasOne(b => b.Source)
         .WithOne(s => s.Book)
         .HasForeignKey<BookSource>(s => s.Id);
        e.Navigation(b => b.Source).IsRequired();
        e.Property(b => b.CommentLanguage).HasMaxLength(8);
        e.Property(b => b.KidsTitles).HasMaxLength(1000);
    }
}

internal sealed class CourseTranslationJobConfiguration : IEntityTypeConfiguration<CourseTranslationJob>
{
    public void Configure(EntityTypeBuilder<CourseTranslationJob> e)
    {
        e.Property(j => j.Language).HasMaxLength(8).IsRequired();
        e.Property(j => j.LastError).HasMaxLength(500);
        // Die Warteschlange: „naechster wartender Auftrag, aelteste zuerst".
        e.HasIndex(j => new { j.Status, j.CreatedAt });
        // „Gibt es fuer (Kurs, Sprache) schon einen offenen Auftrag?"
        e.HasIndex(j => new { j.BookId, j.Language, j.Status });
        e.HasOne(j => j.Book)
         .WithMany()
         .HasForeignKey(j => j.BookId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BookSourceConfiguration : IEntityTypeConfiguration<BookSource>
{
    public void Configure(EntityTypeBuilder<BookSource> e)
    {
        e.ToTable("Books");
    }
}

internal sealed class CatalogGrantConfiguration : IEntityTypeConfiguration<CatalogGrant>
{
    public void Configure(EntityTypeBuilder<CatalogGrant> e)
    {
        e.HasIndex(g => new { g.OwnerUserId, g.SubjectUserId }).IsUnique();
        e.HasIndex(g => new { g.OwnerUserId, g.SubjectGroupId }).IsUnique();
    }
}

internal sealed class CatalogRequestConfiguration : IEntityTypeConfiguration<CatalogRequest>
{
    public void Configure(EntityTypeBuilder<CatalogRequest> e)
    {
        e.HasIndex(r => new { r.OwnerUserId, r.Status });
        e.HasIndex(r => r.RequesterUserId);
    }
}

internal sealed class BookPuzzleConfiguration : IEntityTypeConfiguration<BookPuzzle>
{
    public void Configure(EntityTypeBuilder<BookPuzzle> e)
    {
        e.HasIndex(bp => bp.LineId).IsUnique();
        e.HasIndex(bp => bp.BookFileName);
        e.HasIndex(bp => bp.BookId);
        e.Property(bp => bp.Source).HasMaxLength(16);
        // Pool-Filterung (Daily/Random/Blind) schließt ausgemusterte Puzzles aus.
        e.HasIndex(bp => bp.Retired);

        e.HasOne(bp => bp.Book)
         .WithMany(b => b.Puzzles)
         .HasForeignKey(bp => bp.BookId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BookPuzzleAttemptConfiguration : IEntityTypeConfiguration<BookPuzzleAttempt>
{
    public void Configure(EntityTypeBuilder<BookPuzzleAttempt> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        // Kein zweiter Cascade-Pfad über BookPuzzle (BookPuzzle wird via Book kaskadiert);
        // Restrict vermeidet den MySQL "multiple cascade paths"-Fehler (analog CoursePuzzleResult).
        e.HasOne(a => a.BookPuzzle)
         .WithMany()
         .HasForeignKey(a => a.BookPuzzleId)
         .OnDelete(DeleteBehavior.Restrict);

        e.Property(a => a.AnonymousSessionId).HasMaxLength(36);
        // Spielweise des Versuchs; DB-Default "training" → Altbestand gilt als Trainings-Modus.
        e.Property(a => a.Mode).HasMaxLength(10).HasDefaultValue(SolveMode.Training);
        e.HasIndex(a => new { a.BookPuzzleId, a.AttemptedAt });
        e.HasIndex(a => new { a.BookPuzzleId, a.UserId });
        // Anonyme Lösungen sind „genau einmal je (Puzzle, Session)" — hart per Unique-Index erzwingen
        // (statt nur check-then-insert), sonst blähen Parallel-Requests AnonymousSolvedCount + Webhooks auf.
        // Authentifizierte Versuche haben AnonymousSessionId = NULL → MySQL erlaubt beliebig viele NULLs,
        // d. h. mehrere Versuche je User bleiben möglich; nur anonyme Sessions sind dedupliziert.
        e.HasIndex(a => new { a.BookPuzzleId, a.AnonymousSessionId }).IsUnique();
    }
}

internal sealed class CourseProgressConfiguration : IEntityTypeConfiguration<CourseProgress>
{
    public void Configure(EntityTypeBuilder<CourseProgress> e)
    {
        e.HasOne(cp => cp.User)
         .WithMany(u => u.CourseProgresses)
         .HasForeignKey(cp => cp.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(cp => cp.Book)
         .WithMany()
         .HasForeignKey(cp => cp.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(cp => new { cp.UserId, cp.BookId }).IsUnique();
    }
}

internal sealed class CoursePuzzleResultConfiguration : IEntityTypeConfiguration<CoursePuzzleResult>
{
    public void Configure(EntityTypeBuilder<CoursePuzzleResult> e)
    {
        e.HasOne(cr => cr.User)
         .WithMany(u => u.CoursePuzzleResults)
         .HasForeignKey(cr => cr.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(cr => cr.Book)
         .WithMany()
         .HasForeignKey(cr => cr.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        // Kein zusätzlicher Cascade-Pfad über BookPuzzle: BookPuzzle wird bereits via Book
        // kaskadiert; ein zweiter Cascade-Pfad (BookPuzzle -> CoursePuzzleResult) löst in
        // MySQL/MariaDB einen "multiple cascade paths"-Fehler aus. Daher Restrict.
        e.HasOne(cr => cr.BookPuzzle)
         .WithMany()
         .HasForeignKey(cr => cr.BookPuzzleId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(cr => new { cr.UserId, cr.BookPuzzleId }).IsUnique();
        e.HasIndex(cr => new { cr.UserId, cr.BookId });
    }
}

internal sealed class CoursePinConfiguration : IEntityTypeConfiguration<CoursePin>
{
    public void Configure(EntityTypeBuilder<CoursePin> e)
    {
        e.HasOne(p => p.User)
         .WithMany(u => u.CoursePins)
         .HasForeignKey(p => p.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(p => p.Book)
         .WithMany()
         .HasForeignKey(p => p.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        // Ein Pin pro (User, Buch); Sortier-/Ladeindex nach Anpin-Zeitpunkt.
        e.HasIndex(p => new { p.UserId, p.BookId }).IsUnique();
        e.HasIndex(p => new { p.UserId, p.PinnedAt });
    }
}

internal sealed class CourseShareConfiguration : IEntityTypeConfiguration<CourseShare>
{
    public void Configure(EntityTypeBuilder<CourseShare> e)
    {
        // Cascade NUR über das Buch (ein einziger Cascade-Pfad; ein persönlicher Kurs wird beim
        // Löschen samt seiner Freigaben entfernt). Die beiden AppUser-FKs sind Restrict, sonst
        // erzeugt MySQL/MariaDB einen "multiple cascade paths"-Fehler (analog Friendship).
        e.HasOne(cs => cs.Book)
         .WithMany()
         .HasForeignKey(cs => cs.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(cs => cs.Owner)
         .WithMany()
         .HasForeignKey(cs => cs.OwnerId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(cs => cs.Recipient)
         .WithMany()
         .HasForeignKey(cs => cs.RecipientId)
         .OnDelete(DeleteBehavior.Restrict);

        // Ein Kurs wird an einen Empfänger höchstens einmal geteilt.
        e.HasIndex(cs => new { cs.BookId, cs.RecipientId }).IsUnique();
        // „Welche Kurse sind mit mir geteilt?" (Kursliste/Menü-Sichtbarkeit).
        e.HasIndex(cs => cs.RecipientId);
    }
}

internal sealed class CourseLinkConfiguration : IEntityTypeConfiguration<CourseLink>
{
    public void Configure(EntityTypeBuilder<CourseLink> e)
    {
        e.HasOne(l => l.User)
         .WithMany()
         .HasForeignKey(l => l.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        // Nur EIN Cascade-Pfad von Book (über BookId); LinkedBookId hat bewusst keinen FK
        // (sonst „multiple cascade paths"). Cleanup der Gegenzeile beim Buch-Löschen erfolgt
        // explizit in BookAdminService.DeleteBookAsync.
        e.HasOne(l => l.Book)
         .WithMany()
         .HasForeignKey(l => l.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        // Ein Buch hat je Nutzer höchstens einen verknüpften Partner.
        e.HasIndex(l => new { l.UserId, l.BookId }).IsUnique();
    }
}

internal sealed class CourseInfoViewConfiguration : IEntityTypeConfiguration<CourseInfoView>
{
    public void Configure(EntityTypeBuilder<CourseInfoView> e)
    {
        e.HasOne(iv => iv.User)
         .WithMany()
         .HasForeignKey(iv => iv.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(iv => iv.Book)
         .WithMany()
         .HasForeignKey(iv => iv.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        // Kein zweiter Cascade-Pfad über BookPuzzle (wird via Book kaskadiert) → Restrict,
        // analog CoursePuzzleResult/CourseAttempt (MySQL "multiple cascade paths").
        e.HasOne(iv => iv.BookPuzzle)
         .WithMany()
         .HasForeignKey(iv => iv.BookPuzzleId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(iv => new { iv.UserId, iv.BookPuzzleId }).IsUnique();
        e.HasIndex(iv => new { iv.UserId, iv.BookId });
    }
}

internal sealed class CourseAttemptConfiguration : IEntityTypeConfiguration<CourseAttempt>
{
    public void Configure(EntityTypeBuilder<CourseAttempt> e)
    {
        e.HasOne(a => a.User)
         .WithMany()
         .HasForeignKey(a => a.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(a => a.Book)
         .WithMany()
         .HasForeignKey(a => a.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        // Kein zweiter Cascade-Pfad über BookPuzzle (wird via Book kaskadiert) → Restrict,
        // analog CoursePuzzleResult/BookPuzzleAttempt (MySQL "multiple cascade paths").
        e.HasOne(a => a.BookPuzzle)
         .WithMany()
         .HasForeignKey(a => a.BookPuzzleId)
         .OnDelete(DeleteBehavior.Restrict);

        // Spielweise des Versuchs; DB-Default "training" → Altbestand gilt als Trainings-Modus.
        e.Property(a => a.Mode).HasMaxLength(10).HasDefaultValue(SolveMode.Training);

        // Fenster-Aggregation je User (AttemptedAt >= windowStart).
        e.HasIndex(a => new { a.UserId, a.AttemptedAt });
    }
}

internal sealed class CourseFlashcardMarkConfiguration : IEntityTypeConfiguration<CourseFlashcardMark>
{
    public void Configure(EntityTypeBuilder<CourseFlashcardMark> e)
    {
        e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(m => m.Book).WithMany().HasForeignKey(m => m.BookId).OnDelete(DeleteBehavior.Cascade);
        // Wie CoursePuzzleResult/CalculationTree: Restrict vermeidet den 2. Cascade-Pfad von
        // Book; Linien-/Buch-Löschpfade räumen explizit ab.
        e.HasOne(m => m.BookPuzzle).WithMany().HasForeignKey(m => m.BookPuzzleId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(m => new { m.UserId, m.BookPuzzleId }).IsUnique();
        e.HasIndex(m => new { m.UserId, m.BookId });
    }
}

internal sealed class BookGroupAccessConfiguration : IEntityTypeConfiguration<BookGroupAccess>
{
    public void Configure(EntityTypeBuilder<BookGroupAccess> e)
    {
        e.HasKey(a => new { a.BookId, a.GroupId });
        e.HasOne(a => a.Book)
         .WithMany()
         .HasForeignKey(a => a.BookId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.Group)
         .WithMany()
         .HasForeignKey(a => a.GroupId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(a => a.GroupId);
    }
}

internal sealed class CommentSetConfiguration : IEntityTypeConfiguration<CommentSet>
{
    public void Configure(EntityTypeBuilder<CommentSet> e)
    {
        e.Property(c => c.Language).HasMaxLength(8).IsRequired();
        e.Property(c => c.TranslatedFrom).HasMaxLength(8);
        e.Property(c => c.Model).HasMaxLength(60);
        // Je Partie und Sprache genau EIN Satz. Zwei Saetze derselben Sprache waeren keine
        // Auswahl, sondern eine offene Frage — welcher gilt?
        e.HasIndex(c => new { c.LibraryGameId, c.Language }).IsUnique();
        e.HasIndex(c => new { c.GameAnalysisId, c.Language }).IsUnique();
        e.HasOne(c => c.LibraryGame)
         .WithMany()
         .HasForeignKey(c => c.LibraryGameId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(c => c.GameAnalysis)
         .WithMany()
         .HasForeignKey(c => c.GameAnalysisId)
         .OnDelete(DeleteBehavior.Cascade);
        // Linie eines Kurses (0.547.0): je Linie und Sprache genau EIN Satz, wie bei Partien.
        // Cascade, damit MariaDB mit der Linie auch ihre Uebersetzungen loescht; die Loeschpfade
        // (CourseAuthoringService.RemoveLinesAsync, BookAdminService.DeleteBookAsync, der Rueckbau
        // in CourseService.UploadPersonalCourseAsync) raeumen sie zusaetzlich AUSDRUECKLICH ab —
        // InMemory kaskadiert nicht.
        e.HasIndex(c => new { c.BookPuzzleId, c.Language }).IsUnique();
        e.HasOne(c => c.BookPuzzle)
         .WithMany()
         .HasForeignKey(c => c.BookPuzzleId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CommentTextConfiguration : IEntityTypeConfiguration<CommentText>
{
    public void Configure(EntityTypeBuilder<CommentText> e)
    {
        e.Property(c => c.Text).HasColumnType("LONGTEXT").IsRequired();
        e.HasIndex(c => new { c.CommentSetId, c.Ply }).IsUnique();
        // Fingerabdruck der Vorlage (nur Kurs-Saetze): „gibt es diesen Text schon uebersetzt?" —
        // die Wiederverwendung ueber Kurse hinweg sucht genau danach.
        e.Property(c => c.SourceHash).HasMaxLength(16);
        e.HasIndex(c => c.SourceHash);
        e.HasOne(c => c.CommentSet)
         .WithMany(c => c.Texts)
         .HasForeignKey(c => c.CommentSetId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CourseLineResetConfiguration : IEntityTypeConfiguration<CourseLineReset>
{
    public void Configure(EntityTypeBuilder<CourseLineReset> e)
    {
        e.HasOne(r => r.User)
         .WithMany()
         .HasForeignKey(r => r.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(r => r.Book)
         .WithMany()
         .HasForeignKey(r => r.BookId)
         .OnDelete(DeleteBehavior.Cascade);

        // Wie CourseInfoView: kein zweiter Cascade-Pfad über BookPuzzle → Restrict, abgeräumt über
        // BookPuzzleDependents.
        e.HasOne(r => r.BookPuzzle)
         .WithMany()
         .HasForeignKey(r => r.BookPuzzleId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(r => new { r.UserId, r.BookPuzzleId }).IsUnique();
        e.HasIndex(r => new { r.UserId, r.BookId });
    }
}
