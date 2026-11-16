namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Trainingsvideos (privat je Konto): Import, Lesen, Video-Ausweis.</summary>
    private static void AddTrainingVideoServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RookHub.Api.Services.TrainingVideos.TrainingVideoOptions>(
            configuration.GetSection(RookHub.Api.Services.TrainingVideos.TrainingVideoOptions.Section));
        services.AddSingleton<RookHub.Api.Services.TrainingVideos.TrainingVideoAccessTokens>();
        services.AddScoped<RookHub.Api.Services.TrainingVideos.TrainingVideoImporter>();
        services.AddScoped<RookHub.Api.Services.TrainingVideos.TrainingVideoService>();
    }
}
