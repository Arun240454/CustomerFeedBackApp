using CustomerFeedback.Shared.Configuration;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CustomerFeedback.Shared.Hosting;

/// <summary>Cross-cutting setup every service shares: logging, config layers, auth, errors, database, CORS, health.</summary>
public static class ServiceDefaults
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Configuration.AddLayeredSettings();

        builder.Host.UseSerilog((context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName));

        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddOptions<EncryptionOptions>().Bind(configuration.GetSection(EncryptionOptions.SectionName));
        services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();
        services.AddSingleton(TimeProvider.System);

        var connectionString = configuration.GetConnectionString("CustomerFeedbackDb")
            ?? throw new InvalidOperationException("ConnectionStrings:CustomerFeedbackDb is not configured.");
        services.AddDbContext<FeedbackDbContext>(o => o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddStandardExceptionHandling();
        services.AddJwtAuthentication(configuration);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

        services.AddHealthChecks().AddDbContextCheck<FeedbackDbContext>("database");
        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
            ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information);

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            await next();
        });

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHealthChecks("/health", new HealthCheckOptions());
        return app;
    }
}
