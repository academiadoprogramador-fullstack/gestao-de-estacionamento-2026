using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estacionamento.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT NormalizedEmail
                    FROM AspNetUsers
                    WHERE NormalizedEmail IS NOT NULL
                    GROUP BY NormalizedEmail
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'Cannot create unique EmailIndex: duplicate normalized email addresses exist. Resolve duplicates manually before retrying this migration.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateTable(
                name: "AccessAdministrationControl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessAdministrationControl", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRates_AspNetUsers_PublishedById",
                        column: x => x.PublishedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParkingSpaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingSpaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicIdentifier = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NormalizedPlate = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EncryptedDocument = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DailyRateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyRateSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EnteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attendances_AspNetUsers_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attendances_DailyRates_DailyRateId",
                        column: x => x.DailyRateId,
                        principalTable: "DailyRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyRateConfiguration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CurrentRateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRateConfiguration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRateConfiguration_DailyRates_CurrentRateId",
                        column: x => x.CurrentRateId,
                        principalTable: "DailyRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntryInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncryptedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntryInspections_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryInspections_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotentResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHmac = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    AlgorithmVersion = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotentResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdempotentResults_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdempotentResults_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Occupancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Occupancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Occupancies_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Occupancies_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Occupancies_ParkingSpaces_SpaceId",
                        column: x => x.SpaceId,
                        principalTable: "ParkingSpaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AccessAdministrationControl",
                columns: new[] { "Id" },
                values: new object[] { 1 });

            migrationBuilder.InsertData(
                table: "DailyRateConfiguration",
                columns: new[] { "Id" },
                values: new object[] { 1 });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_DailyRateId",
                table: "Attendances",
                column: "DailyRateId");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_NormalizedPlate_EnteredAtUtc_Id",
                table: "Attendances",
                columns: new[] { "NormalizedPlate", "EnteredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_OperatorId",
                table: "Attendances",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_PublicIdentifier",
                table: "Attendances",
                column: "PublicIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorId",
                table: "AuditEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AttendanceId_OccurredAtUtc_Id",
                table: "AuditEvents",
                columns: new[] { "AttendanceId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRateConfiguration_CurrentRateId",
                table: "DailyRateConfiguration",
                column: "CurrentRateId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRates_PublishedById",
                table: "DailyRates",
                column: "PublishedById");

            migrationBuilder.CreateIndex(
                name: "IX_EntryInspections_ActorId",
                table: "EntryInspections",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryInspections_AttendanceId",
                table: "EntryInspections",
                column: "AttendanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotentResults_ActorId_Operation_Key",
                table: "IdempotentResults",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotentResults_AttendanceId",
                table: "IdempotentResults",
                column: "AttendanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_ActorId",
                table: "Occupancies",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_AttendanceId",
                table: "Occupancies",
                column: "AttendanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_SpaceId",
                table: "Occupancies",
                column: "SpaceId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSpaces_Code",
                table: "ParkingSpaces",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessAdministrationControl");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "DailyRateConfiguration");

            migrationBuilder.DropTable(
                name: "EntryInspections");

            migrationBuilder.DropTable(
                name: "IdempotentResults");

            migrationBuilder.DropTable(
                name: "Occupancies");

            migrationBuilder.DropTable(
                name: "Attendances");

            migrationBuilder.DropTable(
                name: "ParkingSpaces");

            migrationBuilder.DropTable(
                name: "DailyRates");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
