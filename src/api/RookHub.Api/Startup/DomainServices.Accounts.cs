using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Konten, Anmeldung, Rechte, Profil, Admin-Verwaltung.</summary>
    private static void AddAccountServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuthService>();
        services.AddSingleton<EncryptionService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<AuthHandoffService>();
        services.AddScoped<SharedSessionService>();
        services.AddScoped<ViewStateService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<ApiTokenService>();
        services.AddScoped<AdminService>();
        services.AddScoped<RoleAdminService>();
        // Rechte live (eigene Rollen + Gruppenrollen, 60 s gespeichert) — [HasPermission] und GET /api/auth/permissions (0.589.0).
        services.AddScoped<PermissionResolver>();
        services.AddScoped<MenuVisibilityService>();
    }

    /// <summary>Glocke, Web-Push, Freunde, Nachrichten an das Admin-Team, Trainingsziele, Bot-Statistik.</summary>
    private static void AddSocialServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<DiscordLinkService>();   // Vermerke in DiscordLinkReleases (DbContext der Anfrage)
        services.AddScoped<NotificationService>();
        // Web-Push: VAPID-Konfig aus dem Abschnitt „WebPush" (ENV WebPush__Subject/PublicKey/PrivateKey);
        // ohne Schlüssel bleibt Push serverseitig deaktiviert (No-op).
        services.Configure<RookHub.Api.Services.Social.WebPushOptions>(configuration.GetSection("WebPush"));
        services.AddSingleton<IWebPushSender, WebPushSender>();
        services.AddScoped<PushNotificationService>();
        services.AddScoped<AdminMessageService>();
        services.AddScoped<FriendService>();
        services.AddScoped<TrainingGoalService>();
        services.AddScoped<BotStatsService>();
    }
}
