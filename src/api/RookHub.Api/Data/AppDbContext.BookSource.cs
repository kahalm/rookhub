using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RookHub.Api.Models;

namespace RookHub.Api.Data;

// Schutz neuer Bücher ohne BookSource (Tabellensplitting, siehe BookSource) — vom Rest des Kontexts
// getrennt, weil er mit Konfiguration und DbSets nichts zu tun hat.
public partial class AppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // Ein NEUES Buch muss seine BookSource mitbringen (Tabellensplitting, siehe BookSource). Geprüft,
        // sobald ein Book in den Zustand Added kommt — per Add/Update, per Entry(...).State oder als
        // Navigation, die DetectChanges erst beim Speichern entdeckt. Über die Tracker-Ereignisse statt
        // über ChangeTracker.Entries() im SaveChanges: das kostete JEDES SaveChanges der ganzen Anwendung
        // einen zweiten DetectChanges-Lauf.
        ChangeTracker.Tracked += (_, e) => RequireSourceOnNewBook(e.Entry);
        ChangeTracker.StateChanged += (_, e) => RequireSourceOnNewBook(e.Entry);
    }

    /// <summary>Neue Bücher ohne <see cref="Book.Source"/>, deren Fehler abgefangen wurde — damit ein
    /// späteres SaveChanges sie nicht doch noch (relational: mit <c>SourcePgn = NULL</c>) schreibt.</summary>
    private readonly HashSet<Book> _newBooksWithoutSource = new(ReferenceEqualityComparer.Instance);

    private void RequireSourceOnNewBook(EntityEntry entry)
    {
        if (entry.State != EntityState.Added || entry.Entity is not Book { Source: null } book) return;
        _newBooksWithoutSource.Add(book);
        throw MissingSource(book);
    }

    private static InvalidOperationException MissingSource(Book book) => new(
        $"Book \"{book.FileName}\" wird ohne Source angelegt. Beim Anlegen IMMER Source = new BookSource {{ … }} " +
        "setzen (Tabellensplitting, siehe BookSource): relational fiele das nicht auf (SourcePgn = NULL), " +
        "unter InMemory fehlte die BookSource-Zeile (Include liefert null, der Lösch-Stub wirft).");

    /// <summary>Letzte Sperre vor dem Schreiben: ein neues Buch, dessen Fehler beim Tracken abgefangen
    /// wurde, bleibt Added — ohne diese Prüfung ginge es mit dem nächsten SaveChanges trotzdem raus.</summary>
    private void RequireSourceOnPendingBooks()
    {
        if (_newBooksWithoutSource.Count == 0) return;
        _newBooksWithoutSource.RemoveWhere(b => b.Source != null || Entry(b).State != EntityState.Added);
        if (_newBooksWithoutSource.Count > 0) throw MissingSource(_newBooksWithoutSource.First());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RequireSourceOnPendingBooks();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RequireSourceOnPendingBooks();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
