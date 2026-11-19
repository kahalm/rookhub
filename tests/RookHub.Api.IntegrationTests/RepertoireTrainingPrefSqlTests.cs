using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Repertoires;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Reihenfolge der Linien im Repertoire-Trainer (0.996.0, Tabelle <c>RepertoireTrainingPrefs</c>) gegen echtes MariaDB.
///
/// <para>Warum hier: InMemory kennt weder den Unique-Index (UserId, RepertoireId) noch Fremdschlüssel-Cascades. Genau
/// daran hängen aber zwei Zusagen — „eine Zeile je Nutzer und Repertoire“ (zwei Geräte stellen gleichzeitig um) und
/// „die Einstellung stirbt mit dem Repertoire bzw. dem Nutzer“ (zwei Cascade-Pfade auf dieselbe Tabelle; dass MariaDB
/// die annimmt, zeigt erst die angewendete Migration).</para>
/// </summary>
public class RepertoireTrainingPrefSqlTests(RepertoireTrainingPrefSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<RepertoireTrainingPrefSqlFixture>
{
    private readonly List<AppDbContext> _contexts = new();

    public async Task InitializeAsync() => await fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var db in _contexts) await db.DisposeAsync();
    }

    private AppDbContext Fresh()
    {
        var db = fixture.Schema.NewContext();
        _contexts.Add(db);
        return db;
    }

    private async Task<(int User, int Repertoire)> SeedAsync(string name)
    {
        var db = Fresh();
        var user = new AppUser { Username = name, Email = $"{name}@t.local", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var rep = new Repertoire { UserId = user.Id, Name = "Rep " + name };
        db.Repertoires.Add(rep);
        await db.SaveChangesAsync();
        return (user.Id, rep.Id);
    }

    [MySqlFact]
    public async Task SetLineOrder_SpeichertEineZeileJeNutzerUndRepertoire_UndUeberschreibtSie()
    {
        var (user, rep) = await SeedAsync("order1");

        Assert.True(await new RepertoireTrainingService(Fresh()).SetLineOrderAsync(user, rep, "mostTrained"));
        Assert.True(await new RepertoireTrainingService(Fresh()).SetLineOrderAsync(user, rep, "repertoire"));

        var db = Fresh();
        var row = Assert.Single(await db.RepertoireTrainingPrefs.AsNoTracking().ToListAsync());
        Assert.Equal((user, rep, "repertoire"), (row.UserId, row.RepertoireId, row.LineOrder));
        Assert.Equal("repertoire", (await new RepertoireTrainingService(db).GetConfigAsync(user, rep))!.LineOrder);
    }

    [MySqlFact]
    public async Task ZweiteZeileFuerDasselbePaar_LehntDerUniqueIndexAb()
    {
        var (user, rep) = await SeedAsync("order2");
        var db = Fresh();
        db.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = user, RepertoireId = rep, LineOrder = "random" });
        await db.SaveChangesAsync();

        var other = Fresh();
        other.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = user, RepertoireId = rep, LineOrder = "frequency" });

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    /// <summary>Der Wettlauf zweier Geräte: beide sehen „keine Zeile“ und fügen ein. Nachgestellt, indem die Zeile des
    /// anderen GENAU zwischen dem Nachsehen und dem Einfügen des Dienstes entsteht (Interceptor vor dem ersten Speichern).</summary>
    [MySqlFact]
    public async Task SetLineOrder_VerliertDenWettlaufUmDenInsert_UebernimmtDieZeileDesAnderen()
    {
        var (user, rep) = await SeedAsync("order3");
        var rival = new InsertRivalBeforeFirstSave(async () =>
        {
            var other = Fresh();
            other.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = user, RepertoireId = rep, LineOrder = "random" });
            await other.SaveChangesAsync();
        });
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, DbServerVersion.Current).AddInterceptors(rival).Options);
        _contexts.Add(db);

        Assert.True(await new RepertoireTrainingService(db).SetLineOrderAsync(user, rep, "leastTrained"));

        Assert.True(rival.Ran);
        var row = Assert.Single(await Fresh().RepertoireTrainingPrefs.AsNoTracking().ToListAsync());
        Assert.Equal("leastTrained", row.LineOrder);   // eine Zeile, der letzte gewinnt
    }

    private sealed class InsertRivalBeforeFirstSave(Func<Task> insertRival) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Ran) { Ran = true; await insertRival(); }
            return result;
        }
    }

    [MySqlFact]
    public async Task RepertoireGeloescht_NimmtDieEinstellungenAllerNutzerMit()
    {
        var (owner, rep) = await SeedAsync("owner");
        var (friend, otherRep) = await SeedAsync("friend");
        var db = Fresh();
        db.RepertoireShares.Add(new RepertoireShare { RepertoireId = rep, OwnerId = owner, RecipientId = friend });
        await db.SaveChangesAsync();
        Assert.True(await new RepertoireTrainingService(Fresh()).SetLineOrderAsync(owner, rep, "repertoire"));
        Assert.True(await new RepertoireTrainingService(Fresh()).SetLineOrderAsync(friend, rep, "mostTrained"));   // geteiltes fremdes
        Assert.True(await new RepertoireTrainingService(Fresh()).SetLineOrderAsync(friend, otherRep, "random"));   // eigenes

        var del = Fresh();
        del.Repertoires.Remove(await del.Repertoires.SingleAsync(r => r.Id == rep));
        await del.SaveChangesAsync();

        var left = Assert.Single(await Fresh().RepertoireTrainingPrefs.AsNoTracking().ToListAsync());
        Assert.Equal((friend, otherRep, "random"), (left.UserId, left.RepertoireId, left.LineOrder));
    }

    [MySqlFact]
    public async Task NutzerzeileGeloescht_NimmtSeineEinstellungenMit_AuchZuFremdenRepertoires()
    {
        var (owner, rep) = await SeedAsync("owner2");
        var (friend, _) = await SeedAsync("friend2");
        var db = Fresh();
        db.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = owner, RepertoireId = rep, LineOrder = "repertoire" });
        db.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = friend, RepertoireId = rep, LineOrder = "random" });
        await db.SaveChangesAsync();

        // Zeile des Empfängers hart löschen (die Kontolöschung der App anonymisiert nur und räumt selbst ab —
        // hier geht es um den Fremdschlüssel): seine Einstellung zum FREMDEN Repertoire geht mit, die des Besitzers bleibt.
        var del = Fresh();
        del.Repertoires.RemoveRange(await del.Repertoires.Where(r => r.UserId == friend).ToListAsync());
        await del.SaveChangesAsync();
        del.AppUsers.Remove(await del.AppUsers.SingleAsync(u => u.Id == friend));
        await del.SaveChangesAsync();

        var left = Assert.Single(await Fresh().RepertoireTrainingPrefs.AsNoTracking().ToListAsync());
        Assert.Equal((owner, "repertoire"), (left.UserId, left.LineOrder));
    }
}

public sealed class RepertoireTrainingPrefSqlFixture() : MariaDbClassFixture("rtpref", withApp: false);
