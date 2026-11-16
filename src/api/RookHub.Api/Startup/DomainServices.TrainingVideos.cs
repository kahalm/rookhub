namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Trainingsvideos (privat je Konto): Import, Lesen, Video-Ausweis, Notizbuch.</summary>
    private static void AddTrainingVideoServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RookHub.Api.Services.TrainingVideos.TrainingVideoOptions>(
            configuration.GetSection(RookHub.Api.Services.TrainingVideos.TrainingVideoOptions.Section));
        services.AddSingleton<RookHub.Api.Services.TrainingVideos.TrainingVideoAccessTokens>();
        services.AddScoped<RookHub.Api.Services.TrainingVideos.TrainingVideoImporter>();
        services.AddScoped<RookHub.Api.Services.TrainingVideos.TrainingVideoService>();
        // Trainer-Notizbuch: Themen + Eröffnungsstellungen, Abgleich mit dem eigenen Repertoire.
        services.AddScoped<RookHub.Api.Services.Repertoires.RepertoireBoardContinuations>();
        services.AddScoped<RookHub.Api.Services.TrainingVideos.TrainingNotebookService>();
    }
}
