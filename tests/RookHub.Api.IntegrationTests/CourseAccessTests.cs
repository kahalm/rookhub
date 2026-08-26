using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using System.Data.Common;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>Zaehlt die tatsaechlich abgesetzten SQL-Befehle einer Abfrage.</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public int Count { get; private set; }
    public void Reset() => Count = 0;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken ct = default)
    {
        Count++;
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}

/// <summary>
/// Die Kurs-Zugriffsregel fragte frueher bis zu SIEBEN Mal hintereinander die Datenbank —
/// Buch existiert? oeffentlich? eigenes? geteilt? im Verteiler? Everyone-Gruppe? Gruppe? —
/// obwohl alle Zweige ODER-verknuepft sind. Die Pruefung haengt an jedem Kurs-Endpunkt.
///
/// Diese Tests pruefen BEIDES gegen eine echte MariaDB: dass die zusammengefasste Abfrage
/// uebersetzbar ist (InMemory wuerde auch eine untranslatable Fassung gruen melden) und dass
/// jeder Zugriffsweg weiter genau so entscheidet wie zuvor.
/// </summary>
public class CourseAccessTests : IAsyncLifetime
{
    private MariaDbSchema _schema = null!;
    private readonly CommandCounter _counter = new();
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _schema = await MariaDbSchema.CreateAsync("acc");
        await using (var m = _schema.NewContext()) await m.Database.MigrateAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(_schema.ConnectionString, new MySqlServerVersion(new Version(11, 0, 0)))
            .AddInterceptors(_counter)
            .Options;
        _db = new AppDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_schema is not null) await _schema.DisposeAsync();
    }

    private async Task<int> UserAsync(string name)
    {
        var u = new AppUser { Username = name, Email = $"{name}@t.local", PasswordHash = "x" };
        _db.AppUsers.Add(u); await _db.SaveChangesAsync(); return u.Id;
    }

    private async Task<int> BookAsync(string file, bool isPublic = false, int? owner = null)
    {
        var b = new Book { FileName = file, DisplayName = file, IsPublic = isPublic, OwnerUserId = owner };
        _db.Books.Add(b); await _db.SaveChangesAsync(); return b.Id;
    }

    private async Task<bool> CanAccessAsync(int userId, int bookId, bool isAdmin = false)
    {
        _db.ChangeTracker.Clear();
        _counter.Reset();
        return await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin);
    }

    [MySqlFact]
    public async Task EinNichtBerechtigterKostetGenauEineAbfrage()
    {
        var fremd = await UserAsync("fremd");
        var buch = await BookAsync("privat.pgn", owner: await UserAsync("eigner"));

        Assert.False(await CanAccessAsync(fremd, buch));
        // Der teuerste Fall: kein Zweig trifft, frueher wurden dafuer alle sieben abgefragt.
        Assert.Equal(1, _counter.Count);
    }

    [MySqlFact]
    public async Task JederZugriffswegEntscheidetWieZuvor()
    {
        var user = await UserAsync("u");
        var anderer = await UserAsync("a");

        Assert.True(await CanAccessAsync(user, await BookAsync("oeff.pgn", isPublic: true)), "oeffentlich");
        Assert.True(await CanAccessAsync(user, await BookAsync("mein.pgn", owner: user)), "eigenes Buch");
        Assert.False(await CanAccessAsync(user, await BookAsync("fremd.pgn", owner: anderer)), "fremdes Buch");

        var geteilt = await BookAsync("geteilt.pgn", owner: anderer);
        _db.CourseShares.Add(new CourseShare { BookId = geteilt, OwnerId = anderer, RecipientId = user });
        await _db.SaveChangesAsync();
        Assert.True(await CanAccessAsync(user, geteilt), "direkt geteilt");
        Assert.Equal(1, _counter.Count);

        var serie = await BookAsync("serie.pgn", owner: anderer);
        _db.CalcSeriesMembers.Add(new CalcSeriesMember { BookId = serie, UserId = user });
        await _db.SaveChangesAsync();
        Assert.True(await CanAccessAsync(user, serie), "Kalkulations-Verteiler");
    }

    [MySqlFact]
    public async Task GruppenfreigabeGiltAuchUeberEveryone()
    {
        var user = await UserAsync("g");
        var anderer = await UserAsync("g2");

        var perGruppe = await BookAsync("gruppe.pgn", owner: anderer);
        var gruppe = new Group { Name = "Klub" };
        _db.Groups.Add(gruppe); await _db.SaveChangesAsync();
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = perGruppe, GroupId = gruppe.Id });
        await _db.SaveChangesAsync();
        Assert.False(await CanAccessAsync(user, perGruppe), "ohne Mitgliedschaft kein Zugriff");

        _db.UserGroups.Add(new UserGroup { UserId = user, GroupId = gruppe.Id });
        await _db.SaveChangesAsync();
        Assert.True(await CanAccessAsync(user, perGruppe), "mit Mitgliedschaft");
        Assert.Equal(1, _counter.Count);

        var perEveryone = await BookAsync("everyone.pgn", owner: anderer);
        var everyone = new Group { Name = "Everyone", IsEveryone = true };
        _db.Groups.Add(everyone); await _db.SaveChangesAsync();
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = perEveryone, GroupId = everyone.Id });
        await _db.SaveChangesAsync();
        // Everyone gilt OHNE Mitgliedschaftszeile.
        Assert.True(await CanAccessAsync(user, perEveryone), "Everyone-Gruppe");
    }

    [MySqlFact]
    public async Task UnbekanntesBuchIstAuchFuerAdminsGesperrt()
    {
        var admin = await UserAsync("admin");
        Assert.False(await CanAccessAsync(admin, 999_999, isAdmin: true));
        Assert.True(await CanAccessAsync(admin, await BookAsync("egal.pgn", owner: 12345), isAdmin: true));
        Assert.Equal(1, _counter.Count);
    }
}
