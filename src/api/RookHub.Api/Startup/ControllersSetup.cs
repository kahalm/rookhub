using Microsoft.OpenApi;
using RookHub.Api.Services;

namespace RookHub.Api.Startup;

/// <summary>MVC-Controller (Fehlerfilter, Validierungs-Antwort, JSON) und Swagger.</summary>
public static class ControllersSetup
{
    public static IServiceCollection AddRookHubControllers(this IServiceCollection services)
    {
        // Domänen-Ausnahmen (NotFound/DomainValidation/Conflict/Forbidden) → { message } mit 404/400/409/403,
        // noch im Endpoint, damit das Request-Log den echten Status sieht (Begründung in DomainExceptionFilter).
        // Browser-Abbruch (RequestAborted) → 499 ohne Error-Log statt 500 (ClientAbortedExceptionFilter, 0.727.4).
        services.AddControllers(o =>
            {
                o.Filters.Add<RookHub.Api.Filters.DomainExceptionFilter>();
                o.Filters.Add<RookHub.Api.Filters.ClientAbortedExceptionFilter>();
            })
            // Validierungs-400 mit message (= erste Meldung) neben errors/traceId (Codereview A10-006).
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiErrorResponses.InvalidModelState)
            .AddJsonOptions(opts =>
                opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        return services;
    }

    public static IServiceCollection AddRookHubSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "RookHub API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });
            // Microsoft.OpenApi 2.0 (Swashbuckle 10): AddSecurityRequirement nimmt jetzt eine Factory
            // (OpenApiDocument → Requirement); OpenApiReference/ReferenceType wurden durch dedizierte
            // Referenztypen ersetzt — OpenApiSecuritySchemeReference verweist auf das „Bearer"-Schema.
            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer", doc),
                    new List<string>()
                }
            });
        });
        return services;
    }
}
