namespace CustomerFeedback.Shared.Domain;

public static class RoleNames
{
    public const string Customer = "Customer";
    public const string StoreManager = "StoreManager";
}

public static class RoleIds
{
    public const byte Customer = 1;
    public const byte StoreManager = 2;
}

public class Role
{
    public byte RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}

public class User
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Sensitive: encrypted at rest by the DbContext value converter.</summary>
    public string? PhoneNumber { get; set; }
    public byte RoleId { get; set; }
    public Role? Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public short FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class Store
{
    public int StoreId { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? StateRegion { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

public class StoreManager
{
    public int StoreId { get; set; }
    public int UserId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
}

public class FeedbackCategory
{
    public short CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public short SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FeedbackSubmission
{
    public long SubmissionId { get; set; }
    public int StoreId { get; set; }
    public int? CustomerUserId { get; set; }
    public byte OverallRating { get; set; }

    /// <summary>Sensitive: encrypted at rest by the DbContext value converter.</summary>
    public string? Comments { get; set; }
    public DateOnly? VisitDate { get; set; }
    public DateTime SubmittedAtUtc { get; set; }

    /// <summary>Persisted computed column (CAST(SubmittedAtUtc AS date)); never written by the app.</summary>
    public DateOnly SubmittedDateUtc { get; private set; }
    public List<FeedbackItem> Items { get; set; } = [];
}

public class FeedbackItem
{
    public long FeedbackItemId { get; set; }
    public long SubmissionId { get; set; }
    public short CategoryId { get; set; }
    public byte Rating { get; set; }

    /// <summary>Sensitive: encrypted at rest by the DbContext value converter.</summary>
    public string? Comment { get; set; }
    public FeedbackCategory? Category { get; set; }
}

/// <summary>Transactional-outbox row: the durable queue used for asynchronous messaging.</summary>
public class OutboxMessage
{
    public long OutboxMessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Key/value settings loaded into IConfiguration by the database configuration provider.</summary>
public class AppSetting
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
}
