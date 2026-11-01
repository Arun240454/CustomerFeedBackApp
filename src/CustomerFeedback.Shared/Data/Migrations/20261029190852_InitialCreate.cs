using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CustomerFeedback.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    SettingKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.SettingKey);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackCategories",
                columns: table => new
                {
                    CategoryId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackCategories", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    OutboxMessageId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageType = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.OutboxMessageId);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<byte>(type: "tinyint", nullable: false),
                    RoleName = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StateRegion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.StoreId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    RoleId = table.Column<byte>(type: "tinyint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    FailedLoginCount = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    LockoutEndUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    LastLoginUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.CheckConstraint("CK_Users_Email", "[Email] LIKE N'_%@_%._%'");
                    table.ForeignKey(
                        name: "FK_Users_Roles",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackSubmissions",
                columns: table => new
                {
                    SubmissionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    CustomerUserId = table.Column<int>(type: "int", nullable: true),
                    OverallRating = table.Column<byte>(type: "tinyint", nullable: false),
                    Comments = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    VisitDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    SubmittedDateUtc = table.Column<DateOnly>(type: "date", nullable: false, computedColumnSql: "CAST([SubmittedAtUtc] AS date)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackSubmissions", x => x.SubmissionId);
                    table.CheckConstraint("CK_FeedbackSubmissions_OverallRating", "[OverallRating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_FeedbackSubmissions_Stores",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackSubmissions_Users",
                        column: x => x.CustomerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreManagers",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreManagers", x => new { x.StoreId, x.UserId });
                    table.ForeignKey(
                        name: "FK_StoreManagers_Stores",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreManagers_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackItems",
                columns: table => new
                {
                    FeedbackItemId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<long>(type: "bigint", nullable: false),
                    CategoryId = table.Column<short>(type: "smallint", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackItems", x => x.FeedbackItemId);
                    table.CheckConstraint("CK_FeedbackItems_Rating", "[Rating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_FeedbackItems_Categories",
                        column: x => x.CategoryId,
                        principalTable: "FeedbackCategories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackItems_Submissions",
                        column: x => x.SubmissionId,
                        principalTable: "FeedbackSubmissions",
                        principalColumn: "SubmissionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "SettingKey", "SettingValue" },
                values: new object[,]
                {
                    { "Analytics:DefaultWindowDays", "30" },
                    { "Security:LockoutMinutes", "15" },
                    { "Security:MaxFailedLogins", "5" }
                });

            migrationBuilder.InsertData(
                table: "FeedbackCategories",
                columns: new[] { "CategoryId", "CategoryName", "Description", "IsActive", "SortOrder" },
                values: new object[,]
                {
                    { (short)1, "Product Quality", "Quality and availability of products", true, (short)1 },
                    { (short)2, "Staff Service", "Friendliness and helpfulness of staff", true, (short)2 },
                    { (short)3, "Cleanliness", "Store and facility cleanliness", true, (short)3 },
                    { (short)4, "Pricing", "Value for money and price clarity", true, (short)4 },
                    { (short)5, "Wait Time", "Queue and checkout wait times", true, (short)5 },
                    { (short)6, "Other", "Anything else", true, (short)6 }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "RoleId", "RoleName" },
                values: new object[,]
                {
                    { (byte)1, "Customer" },
                    { (byte)2, "StoreManager" }
                });

            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "StoreId", "AddressLine1", "City", "CountryCode", "CreatedAtUtc", "IsActive", "PostalCode", "StateRegion", "StoreCode", "StoreName" },
                values: new object[,]
                {
                    { 1, null, "New Delhi", "IN", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null, "DEL-001", "Connaught Place Store" },
                    { 2, null, "Bengaluru", "IN", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null, "BLR-001", "Indiranagar Store" }
                });

            migrationBuilder.CreateIndex(
                name: "UQ_FeedbackCategories_Name",
                table: "FeedbackCategories",
                column: "CategoryName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackItems_Category",
                table: "FeedbackItems",
                columns: new[] { "CategoryId", "SubmissionId" })
                .Annotation("SqlServer:Include", new[] { "Rating" });

            migrationBuilder.CreateIndex(
                name: "UQ_FeedbackItems_Submission_Category",
                table: "FeedbackItems",
                columns: new[] { "SubmissionId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_Customer",
                table: "FeedbackSubmissions",
                columns: new[] { "CustomerUserId", "SubmittedAtUtc" },
                descending: new[] { false, true },
                filter: "[CustomerUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_Store_Date",
                table: "FeedbackSubmissions",
                columns: new[] { "StoreId", "SubmittedDateUtc" })
                .Annotation("SqlServer:Include", new[] { "OverallRating", "CustomerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "OutboxMessageId",
                filter: "[ProcessedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_Roles_RoleName",
                table: "Roles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreManagers_User",
                table: "StoreManagers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_Stores_StoreCode",
                table: "Stores",
                column: "StoreCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UQ_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "FeedbackItems");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "StoreManagers");

            migrationBuilder.DropTable(
                name: "FeedbackCategories");

            migrationBuilder.DropTable(
                name: "FeedbackSubmissions");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
