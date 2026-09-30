using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RookHub.Api.Models;

namespace RookHub.Api.Data.Configurations;

// Konten und Verwaltung: Nutzer, Profile, Gruppen, Rollen, Tokens, Menü-Freigaben, CI-Stand.

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> e)
    {
        e.HasIndex(u => u.Username).IsUnique();
        e.HasIndex(u => u.Email).IsUnique();
    }
}

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> e)
    {
        e.HasOne(p => p.User)
         .WithOne(u => u.Profile)
         .HasForeignKey<UserProfile>(p => p.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        // Eine Discord-ID ist höchstens einem RookHub-User zugeordnet.
        // (MySQL behandelt mehrere NULLs als verschieden → nicht-verknüpfte Profile sind ok.)
        e.HasIndex(p => p.DiscordId).IsUnique();
    }
}

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> e)
    {
        e.HasIndex(g => g.Name).IsUnique();
    }
}

internal sealed class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> e)
    {
        e.HasKey(ug => new { ug.UserId, ug.GroupId });
        e.HasOne(ug => ug.User)
         .WithMany(u => u.Groups)
         .HasForeignKey(ug => ug.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(ug => ug.Group)
         .WithMany(g => g.Members)
         .HasForeignKey(ug => ug.GroupId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> e)
    {
        e.HasIndex(r => r.Key).IsUnique();
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> e)
    {
        e.HasKey(ur => new { ur.UserId, ur.RoleId });
        e.HasOne(ur => ur.User)
         .WithMany(u => u.Roles)
         .HasForeignKey(ur => ur.UserId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(ur => ur.Role)
         .WithMany(r => r.Users)
         .HasForeignKey(ur => ur.RoleId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GroupRoleConfiguration : IEntityTypeConfiguration<GroupRole>
{
    public void Configure(EntityTypeBuilder<GroupRole> e)
    {
        e.HasKey(gr => new { gr.GroupId, gr.RoleId });
        e.HasOne(gr => gr.Group).WithMany().HasForeignKey(gr => gr.GroupId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(gr => gr.Role).WithMany().HasForeignKey(gr => gr.RoleId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(gr => gr.RoleId);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> e)
    {
        e.HasKey(rp => new { rp.RoleId, rp.Permission });
        e.HasOne(rp => rp.Role)
         .WithMany(r => r.Permissions)
         .HasForeignKey(rp => rp.RoleId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CiBuildReportConfiguration : IEntityTypeConfiguration<CiBuildReport>
{
    public void Configure(EntityTypeBuilder<CiBuildReport> e)
    {
        e.HasKey(x => x.Repo);
        e.Property(x => x.Repo).HasMaxLength(100);
        e.Property(x => x.Sha).HasMaxLength(64);
        e.Property(x => x.Ref).HasMaxLength(200);
    }
}

internal sealed class MenuItemSettingConfiguration : IEntityTypeConfiguration<MenuItemSetting>
{
    public void Configure(EntityTypeBuilder<MenuItemSetting> e)
    {
        e.HasKey(s => s.ItemKey);
        e.Property(s => s.ItemKey).HasMaxLength(50);
    }
}

internal sealed class MenuItemGroupAccessConfiguration : IEntityTypeConfiguration<MenuItemGroupAccess>
{
    public void Configure(EntityTypeBuilder<MenuItemGroupAccess> e)
    {
        e.HasKey(a => new { a.ItemKey, a.GroupId });
        e.Property(a => a.ItemKey).HasMaxLength(50);
        e.HasOne(a => a.Setting)
         .WithMany(s => s.Groups)
         .HasForeignKey(a => a.ItemKey)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.Group)
         .WithMany()
         .HasForeignKey(a => a.GroupId)
         .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(a => a.GroupId);
    }
}

internal sealed class UserApiTokenConfiguration : IEntityTypeConfiguration<UserApiToken>
{
    public void Configure(EntityTypeBuilder<UserApiToken> e)
    {
        // Unique-Index auf TokenHash → O(1)-Lookup bei jeder Authentifizierung.
        e.HasIndex(t => t.TokenHash).IsUnique();
        e.HasIndex(t => new { t.UserId, t.Name });
        e.HasOne(t => t.User)
         .WithMany()
         .HasForeignKey(t => t.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> e)
    {
        // Unique-Index auf TokenHash → O(1)-Lookup beim Einloesen.
        e.HasIndex(t => t.TokenHash).IsUnique();
        e.HasIndex(t => t.UserId);
        e.HasOne(t => t.User)
         .WithMany()
         .HasForeignKey(t => t.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuthHandoffTokenConfiguration : IEntityTypeConfiguration<AuthHandoffToken>
{
    public void Configure(EntityTypeBuilder<AuthHandoffToken> e)
    {
        // Wie beim Passwort-Reset: Unique-Index auf den Hash → O(1) beim Einloesen.
        e.HasIndex(t => t.TokenHash).IsUnique();
        e.HasIndex(t => t.UserId);
        e.HasOne(t => t.User)
         .WithMany()
         .HasForeignKey(t => t.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
