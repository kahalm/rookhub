using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;

namespace RookHub.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Repertoire> Repertoires => Set<Repertoire>();
    public DbSet<RepertoireFile> RepertoireFiles => Set<RepertoireFile>();
    public DbSet<TournamentSubscription> TournamentSubscriptions => Set<TournamentSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<UserProfile>(e =>
        {
            e.HasOne(p => p.User)
             .WithOne(u => u.Profile)
             .HasForeignKey<UserProfile>(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Friendship>(e =>
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
        });

        modelBuilder.Entity<Repertoire>(e =>
        {
            e.HasOne(r => r.User)
             .WithMany(u => u.Repertoires)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepertoireFile>(e =>
        {
            e.HasOne(rf => rf.Repertoire)
             .WithMany(r => r.Files)
             .HasForeignKey(rf => rf.RepertoireId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TournamentSubscription>(e =>
        {
            e.HasOne(ts => ts.User)
             .WithMany(u => u.TournamentSubscriptions)
             .HasForeignKey(ts => ts.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ts => new { ts.UserId, ts.CrawlerTournamentId }).IsUnique();
        });
    }
}
