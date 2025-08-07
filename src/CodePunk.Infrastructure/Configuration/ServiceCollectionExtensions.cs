using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodePunk.Infrastructure.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodePunkServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Add database with performance optimizations
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=codepunk.db";

        services.AddDbContext<CodePunkDbContext>(options =>
        {
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });
            
            // Performance optimizations
            options.EnableServiceProviderCaching();
            options.EnableDetailedErrors(false); // Disable in production
            
            // Disable change tracking for read queries by default
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // Add repositories
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IFileHistoryRepository, FileHistoryRepository>();

        // Add services
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IFileHistoryService, FileHistoryService>();

        return services;
    }

    public static async Task<IServiceProvider> EnsureDatabaseCreatedAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodePunkDbContext>();
        await context.Database.EnsureCreatedAsync();
        return serviceProvider;
    }
}
