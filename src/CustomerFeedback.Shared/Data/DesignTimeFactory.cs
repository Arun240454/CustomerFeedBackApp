using CustomerFeedback.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace CustomerFeedback.Shared.Data;

/// <summary>
/// Used only by `dotnet ef`. Needs no secrets: migrations never touch encrypted values,
/// so a throwaway key is sufficient. The connection string can be overridden with
/// the ConnectionStrings__CustomerFeedbackDb environment variable.
/// </summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<FeedbackDbContext>
{
    private const string DefaultConnection = "Server=localhost;Database=CustomerFeedbackDB;Trusted_Connection=True;TrustServerCertificate=True;";

    public FeedbackDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__CustomerFeedbackDb") ?? DefaultConnection;
        var options = new DbContextOptionsBuilder<FeedbackDbContext>().UseSqlServer(connection).Options;
        var encryptor = new AesGcmFieldEncryptor(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(new byte[32]) }));
        return new FeedbackDbContext(options, encryptor);
    }
}
