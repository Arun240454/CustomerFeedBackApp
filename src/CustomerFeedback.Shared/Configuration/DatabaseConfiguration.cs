using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CustomerFeedback.Shared.Configuration;

/// <summary>Loads operational settings from dbo.AppSettings so they can change without a redeploy.</summary>
public sealed class DatabaseConfigurationSource(string? connectionString) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new DatabaseConfigurationProvider(connectionString);
}

public sealed class DatabaseConfigurationProvider(string? connectionString) : ConfigurationProvider
{
    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();
                using var command = new SqlCommand("SELECT SettingKey, SettingValue FROM dbo.AppSettings", connection);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    data[reader.GetString(0)] = reader.GetString(1);
                }
            }
            catch (SqlException)
            {
                // Database or table not created yet (first run, before migrations): fall back to file/env settings.
            }
        }

        Data = data;
    }
}

public static class ConfigurationExtensions
{
    /// <summary>
    /// Layering, lowest to highest precedence: appsettings.json, appsettings.{Env}.json, user-secrets,
    /// service.ini (property file), database AppSettings, environment variables, command line.
    /// Environment variables are re-applied last so deployments can always override database values.
    /// </summary>
    public static ConfigurationManager AddLayeredSettings(this ConfigurationManager configuration)
    {
        configuration.AddIniFile("service.ini", optional: true, reloadOnChange: false);
        ((IConfigurationBuilder)configuration).Sources.Add(new DatabaseConfigurationSource(configuration.GetConnectionString("CustomerFeedbackDb")));
        configuration.AddEnvironmentVariables();
        return configuration;
    }
}
