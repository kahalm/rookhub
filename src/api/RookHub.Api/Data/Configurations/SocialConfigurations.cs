using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Freunde, Herausforderungen, Benachrichtigungen, Nachrichten und Web-Push.

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> e)
    {
        e.HasOne(f => f.Requester)
         .WithMany()
         .HasForeignKey(f => f.RequesterId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(f => f.Addressee)
         .WithMany()
         .HasForeignKey(f => f.AddresseeId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();

        // Richtungsunabhaengige Eindeutigkeit: genau eine Zeile pro ungeordnetem Paar.
        // Der direktionale Index oben verhindert NICHT, dass gleichzeitige A->B und
        // B->A zwei Zeilen erzeugen. STORED computed columns + Unique-Index loesen das
        // auf DB-Ebene (MariaDB kennt keine gefilterten/funktionalen Indizes) — analog
        // zur Crawler-ActiveKey-Loesung. Greift mit dem catch(DbUpdateException) in
        // FriendService.SendRequestAsync zusammen, das dann sauber 409/Fehler liefert.
        e.Property<int>("PairLow").HasComputedColumnSql("LEAST(RequesterId, AddresseeId)", stored: true);
        e.Property<int>("PairHigh").HasComputedColumnSql("GREATEST(RequesterId, AddresseeId)", stored: true);
        e.HasIndex("PairLow", "PairHigh").IsUnique();
    }
}

internal sealed class PuzzleChallengeConfiguration : IEntityTypeConfiguration<PuzzleChallenge>
{
    public void Configure(EntityTypeBuilder<PuzzleChallenge> e)
    {
        // Zwei FKs auf AppUser → Restrict (wie Friendship), sonst mehrere Cascade-Pfade.
        // Konten werden ohnehin anonymisiert statt hart gelöscht (DSGVO-Flow).
        e.HasOne(c => c.FromUser)
         .WithMany()
         .HasForeignKey(c => c.FromUserId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(c => c.ToUser)
         .WithMany()
         .HasForeignKey(c => c.ToUserId)
         .OnDelete(DeleteBehavior.Restrict);

        // PuzzleId ist polymorph (Puzzles ODER BookPuzzles, je nach Source) → bewusst KEIN FK.
        // Existenz wird je Quelle im ChallengeService geprüft.

        // Posteingang/Badge: offene Challenges an einen Empfänger.
        e.HasIndex(c => new { c.ToUserId, c.Status });
        // Gesendete Challenges eines Absenders.
        e.HasIndex(c => c.FromUserId);
        // Dedup offener Challenges je Quelle+Puzzle.
        e.HasIndex(c => new { c.Source, c.PuzzleId });
    }
}

internal sealed class RevengeNotificationConfiguration : IEntityTypeConfiguration<RevengeNotification>
{
    public void Configure(EntityTypeBuilder<RevengeNotification> e)
    {
        // Zwei FKs auf AppUser → Restrict (wie Friendship/PuzzleChallenge).
        e.HasOne(n => n.AvengerUser)
         .WithMany()
         .HasForeignKey(n => n.AvengerUserId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(n => n.TargetUser)
         .WithMany()
         .HasForeignKey(n => n.TargetUserId)
         .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(n => n.Puzzle)
         .WithMany()
         .HasForeignKey(n => n.PuzzleId)
         .OnDelete(DeleteBehavior.Restrict);

        // Feed + Badge: (un)gelesene Benachrichtigungen eines Targets.
        e.HasIndex(n => new { n.TargetUserId, n.SeenAt });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        // Generische In-App-Benachrichtigung; wird mit dem User gelöscht.
        e.HasOne(n => n.User)
         .WithMany()
         .HasForeignKey(n => n.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.Property(n => n.Type).HasMaxLength(60);
        e.Property(n => n.Link).HasMaxLength(300);

        // Feed + Badge: (un)gelesene Benachrichtigungen eines Users.
        e.HasIndex(n => new { n.UserId, n.SeenAt });
    }
}

internal sealed class AdminMessageConfiguration : IEntityTypeConfiguration<AdminMessage>
{
    public void Configure(EntityTypeBuilder<AdminMessage> e)
    {
        // Thread-Schlüssel = der Nicht-Admin-Teilnehmer; wird mit dem User gelöscht.
        e.HasOne(m => m.User)
         .WithMany()
         .HasForeignKey(m => m.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        e.Property(m => m.Body).HasMaxLength(4000);

        // Thread laden (UserId, chronologisch) + Admin-Badge (ungelesene User-Antworten).
        e.HasIndex(m => new { m.UserId, m.CreatedAt });
        e.HasIndex(m => new { m.FromAdmin, m.SeenByAdminAt });
    }
}

internal sealed class MessageThreadConfiguration : IEntityTypeConfiguration<MessageThread>
{
    public void Configure(EntityTypeBuilder<MessageThread> e)
    {
        // Schlüssel = User; wird mit dem User gelöscht. ClaimedByAdminId bewusst ohne FK.
        e.HasKey(t => t.UserId);
        e.HasOne(t => t.User)
         .WithMany()
         .HasForeignKey(t => t.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserPushSubscriptionConfiguration : IEntityTypeConfiguration<UserPushSubscription>
{
    public void Configure(EntityTypeBuilder<UserPushSubscription> e)
    {
        e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(s => s.Endpoint).IsUnique();   // eine Subscription je Endpoint
        e.HasIndex(s => s.UserId);
    }
}

internal sealed class NotificationPushSettingConfiguration : IEntityTypeConfiguration<NotificationPushSetting>
{
    public void Configure(EntityTypeBuilder<NotificationPushSetting> e)
    {
        e.HasKey(s => s.UserId);                  // genau eine Zeile je User
        e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
