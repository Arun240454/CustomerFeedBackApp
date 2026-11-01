using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CustomerFeedback.Shared.Data;

public class FeedbackDbContext(DbContextOptions<FeedbackDbContext> options, IFieldEncryptor encryptor)
    : DbContext(options)
{
    private static readonly DateTime SeedDate = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreManager> StoreManagers => Set<StoreManager>();
    public DbSet<FeedbackCategory> FeedbackCategories => Set<FeedbackCategory>();
    public DbSet<FeedbackSubmission> FeedbackSubmissions => Set<FeedbackSubmission>();
    public DbSet<FeedbackItem> FeedbackItems => Set<FeedbackItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Sensitive columns are stored as AES-GCM ciphertext (ASCII), so they are varchar and wider than the plain-text lengths.
        var encrypted = new ValueConverter<string?, string>(v => encryptor.Encrypt(v!), v => encryptor.Decrypt(v));

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.RoleId);
            e.Property(x => x.RoleId).ValueGeneratedNever();
            e.Property(x => x.RoleName).HasMaxLength(30).IsUnicode(false).IsRequired();
            e.HasIndex(x => x.RoleName).IsUnique().HasDatabaseName("UQ_Roles_RoleName");
            e.HasData(
                new Role { RoleId = RoleIds.Customer, RoleName = RoleNames.Customer },
                new Role { RoleId = RoleIds.StoreManager, RoleName = RoleNames.StoreManager });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users", t => t.HasCheckConstraint("CK_Users_Email", "[Email] LIKE N'_%@_%._%'"));
            e.HasKey(x => x.UserId);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UQ_Users_Email");
            e.Property(x => x.PasswordHash).HasMaxLength(255).IsUnicode(false).IsRequired();
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(255).IsUnicode(false).HasConversion(encrypted);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.EmailConfirmed).HasDefaultValue(false);
            e.Property(x => x.FailedLoginCount).HasDefaultValue((short)0);
            e.Property(x => x.LockoutEndUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.LastLoginUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Users_Roles");
            e.HasIndex(x => x.RoleId).HasDatabaseName("IX_Users_RoleId");
        });

        modelBuilder.Entity<Store>(e =>
        {
            e.ToTable("Stores");
            e.HasKey(x => x.StoreId);
            e.Property(x => x.StoreCode).HasMaxLength(20).IsUnicode(false).IsRequired();
            e.HasIndex(x => x.StoreCode).IsUnique().HasDatabaseName("UQ_Stores_StoreCode");
            e.Property(x => x.StoreName).HasMaxLength(150).IsRequired();
            e.Property(x => x.AddressLine1).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.StateRegion).HasMaxLength(100);
            e.Property(x => x.PostalCode).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasData(
                new Store { StoreId = 1, StoreCode = "DEL-001", StoreName = "Connaught Place Store", City = "New Delhi", CountryCode = "IN", CreatedAtUtc = SeedDate },
                new Store { StoreId = 2, StoreCode = "BLR-001", StoreName = "Indiranagar Store", City = "Bengaluru", CountryCode = "IN", CreatedAtUtc = SeedDate });
        });

        modelBuilder.Entity<StoreManager>(e =>
        {
            e.ToTable("StoreManagers");
            e.HasKey(x => new { x.StoreId, x.UserId });
            e.Property(x => x.AssignedAtUtc).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_StoreManagers_Stores");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_StoreManagers_Users");
            e.HasIndex(x => x.UserId).HasDatabaseName("IX_StoreManagers_User");
        });

        modelBuilder.Entity<FeedbackCategory>(e =>
        {
            e.ToTable("FeedbackCategories");
            e.HasKey(x => x.CategoryId);
            e.Property(x => x.CategoryName).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.CategoryName).IsUnique().HasDatabaseName("UQ_FeedbackCategories_Name");
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.SortOrder).HasDefaultValue((short)0);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasData(
                new FeedbackCategory { CategoryId = 1, CategoryName = "Product Quality", Description = "Quality and availability of products", SortOrder = 1 },
                new FeedbackCategory { CategoryId = 2, CategoryName = "Staff Service", Description = "Friendliness and helpfulness of staff", SortOrder = 2 },
                new FeedbackCategory { CategoryId = 3, CategoryName = "Cleanliness", Description = "Store and facility cleanliness", SortOrder = 3 },
                new FeedbackCategory { CategoryId = 4, CategoryName = "Pricing", Description = "Value for money and price clarity", SortOrder = 4 },
                new FeedbackCategory { CategoryId = 5, CategoryName = "Wait Time", Description = "Queue and checkout wait times", SortOrder = 5 },
                new FeedbackCategory { CategoryId = 6, CategoryName = "Other", Description = "Anything else", SortOrder = 6 });
        });

        modelBuilder.Entity<FeedbackSubmission>(e =>
        {
            e.ToTable("FeedbackSubmissions", t => t.HasCheckConstraint("CK_FeedbackSubmissions_OverallRating", "[OverallRating] BETWEEN 1 AND 5"));
            e.HasKey(x => x.SubmissionId);
            e.Property(x => x.Comments).IsUnicode(false).HasConversion(encrypted);
            e.Property(x => x.SubmittedAtUtc).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.SubmittedDateUtc).HasComputedColumnSql("CAST([SubmittedAtUtc] AS date)", stored: true);
            e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_FeedbackSubmissions_Stores");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_FeedbackSubmissions_Users");
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FeedbackItems_Submissions");

            // Trend summary: store + day range, covering the overall rating.
            e.HasIndex(x => new { x.StoreId, x.SubmittedDateUtc }).IncludeProperties(x => new { x.OverallRating, x.CustomerUserId })
                .HasDatabaseName("IX_FeedbackSubmissions_Store_Date");
            // "My feedback" history lookup.
            e.HasIndex(x => new { x.CustomerUserId, x.SubmittedAtUtc }).IsDescending(false, true)
                .HasFilter("[CustomerUserId] IS NOT NULL").HasDatabaseName("IX_FeedbackSubmissions_Customer");
        });

        modelBuilder.Entity<FeedbackItem>(e =>
        {
            e.ToTable("FeedbackItems", t => t.HasCheckConstraint("CK_FeedbackItems_Rating", "[Rating] BETWEEN 1 AND 5"));
            e.HasKey(x => x.FeedbackItemId);
            e.Property(x => x.Comment).IsUnicode(false).HasConversion(encrypted);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_FeedbackItems_Categories");
            e.HasIndex(x => new { x.SubmissionId, x.CategoryId }).IsUnique().HasDatabaseName("UQ_FeedbackItems_Submission_Category");
            e.HasIndex(x => new { x.CategoryId, x.SubmissionId }).IncludeProperties(x => x.Rating).HasDatabaseName("IX_FeedbackItems_Category");
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages");
            e.HasKey(x => x.OutboxMessageId);
            e.Property(x => x.MessageType).HasMaxLength(200).IsUnicode(false).IsRequired();
            e.Property(x => x.Payload).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ProcessedAtUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => x.OutboxMessageId).HasFilter("[ProcessedAtUtc] IS NULL").HasDatabaseName("IX_OutboxMessages_Pending");
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("AppSettings");
            e.HasKey(x => x.SettingKey);
            e.Property(x => x.SettingKey).HasMaxLength(200);
            e.Property(x => x.SettingValue).HasMaxLength(2000).IsRequired();
            e.HasData(
                new AppSetting { SettingKey = "Security:MaxFailedLogins", SettingValue = "5" },
                new AppSetting { SettingKey = "Security:LockoutMinutes", SettingValue = "15" },
                new AppSetting { SettingKey = "Analytics:DefaultWindowDays", SettingValue = "30" });
        });
    }
}
