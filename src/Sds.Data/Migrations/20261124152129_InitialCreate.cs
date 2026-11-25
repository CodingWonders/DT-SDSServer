using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sds.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ComputerName = table.Column<string>(type: "TEXT", nullable: true),
                    Phase = table.Column<string>(type: "TEXT", nullable: true),
                    ImageName = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientUnattends",
                columns: table => new
                {
                    Architecture = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientUnattends", x => x.Architecture);
                });

            migrationBuilder.CreateTable(
                name: "Computers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false, collation: "NOCASE"),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: true),
                    SmbiosGuid = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", nullable: true),
                    JoinDomain = table.Column<bool>(type: "INTEGER", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationalUnit = table.Column<string>(type: "TEXT", nullable: true),
                    User = table.Column<string>(type: "TEXT", nullable: true),
                    JoinRights = table.Column<string>(type: "TEXT", nullable: false),
                    ReferralServer = table.Column<string>(type: "TEXT", nullable: true),
                    BootProgram = table.Column<string>(type: "TEXT", nullable: true),
                    BootImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    ClientUnattend = table.Column<string>(type: "TEXT", nullable: true),
                    ImageGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    InstallImageId = table.Column<int>(type: "INTEGER", nullable: true),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    DirectorySync = table.Column<string>(type: "TEXT", nullable: false),
                    DistinguishedName = table.Column<string>(type: "TEXT", nullable: true),
                    DirectoryError = table.Column<string>(type: "TEXT", nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Computers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Applicability = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", nullable: false),
                    InfName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    ClassName = table.Column<string>(type: "TEXT", nullable: true),
                    ClassGuid = table.Column<Guid>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    DriverDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogFile = table.Column<string>(type: "TEXT", nullable: true),
                    BootCritical = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    InfHash = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingDevices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: true),
                    SmbiosGuid = table.Column<Guid>(type: "TEXT", nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedName = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "DriverGroupFilters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DriverGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Policy = table.Column<string>(type: "TEXT", nullable: false),
                    Values = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverGroupFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverGroupFilters_DriverGroups_DriverGroupId",
                        column: x => x.DriverGroupId,
                        principalTable: "DriverGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DriverGroupPackages",
                columns: table => new
                {
                    GroupsId = table.Column<int>(type: "INTEGER", nullable: false),
                    PackagesId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverGroupPackages", x => new { x.GroupsId, x.PackagesId });
                    table.ForeignKey(
                        name: "FK_DriverGroupPackages_DriverGroups_GroupsId",
                        column: x => x.GroupsId,
                        principalTable: "DriverGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverGroupPackages_DriverPackages_PackagesId",
                        column: x => x.PackagesId,
                        principalTable: "DriverPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DriverHardwareIds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DriverPackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HardwareId = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverHardwareIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverHardwareIds_DriverPackages_DriverPackageId",
                        column: x => x.DriverPackageId,
                        principalTable: "DriverPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImageGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", nullable: true),
                    EditionId = table.Column<string>(type: "TEXT", nullable: true),
                    InstallationType = table.Column<string>(type: "TEXT", nullable: true),
                    ProductType = table.Column<string>(type: "TEXT", nullable: true),
                    Flags = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    ServicePackLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Hal = table.Column<string>(type: "TEXT", nullable: true),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultLanguage = table.Column<string>(type: "TEXT", nullable: true),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FileCount = table.Column<long>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnattendFile = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: true),
                    SourceIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Images_ImageGroups_ImageGroupId",
                        column: x => x.ImageGroupId,
                        principalTable: "ImageGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MulticastTransmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    InstallImageId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    StartClientThreshold = table.Column<int>(type: "INTEGER", nullable: true),
                    StartTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActivatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MulticastTransmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MulticastTransmissions_Images_InstallImageId",
                        column: x => x.InstallImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MulticastClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MulticastTransmissionId = table.Column<int>(type: "INTEGER", nullable: false),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ComputerName = table.Column<string>(type: "TEXT", nullable: true),
                    PercentComplete = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeftUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MulticastClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MulticastClients_MulticastTransmissions_MulticastTransmissionId",
                        column: x => x.MulticastTransmissionId,
                        principalTable: "MulticastTransmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientLogs_TimestampUtc",
                table: "ClientLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Computers_MacAddress",
                table: "Computers",
                column: "MacAddress",
                unique: true,
                filter: "MacAddress IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Computers_Name",
                table: "Computers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Computers_SmbiosGuid",
                table: "Computers",
                column: "SmbiosGuid",
                unique: true,
                filter: "SmbiosGuid IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroupFilters_DriverGroupId",
                table: "DriverGroupFilters",
                column: "DriverGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroupPackages_PackagesId",
                table: "DriverGroupPackages",
                column: "PackagesId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroups_Name",
                table: "DriverGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverHardwareIds_DriverPackageId",
                table: "DriverHardwareIds",
                column: "DriverPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverHardwareIds_HardwareId",
                table: "DriverHardwareIds",
                column: "HardwareId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverPackages_FolderName",
                table: "DriverPackages",
                column: "FolderName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverPackages_InfHash_Architecture",
                table: "DriverPackages",
                columns: new[] { "InfHash", "Architecture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageGroups_Name",
                table: "ImageGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_ImageGroupId_FileName",
                table: "Images",
                columns: new[] { "ImageGroupId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CreatedUtc",
                table: "Jobs",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MulticastClients_MulticastTransmissionId",
                table: "MulticastClients",
                column: "MulticastTransmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_MulticastTransmissions_InstallImageId",
                table: "MulticastTransmissions",
                column: "InstallImageId");

            migrationBuilder.CreateIndex(
                name: "IX_MulticastTransmissions_Name",
                table: "MulticastTransmissions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingDevices_MacAddress",
                table: "PendingDevices",
                column: "MacAddress");

            migrationBuilder.CreateIndex(
                name: "IX_PendingDevices_SmbiosGuid",
                table: "PendingDevices",
                column: "SmbiosGuid");

            migrationBuilder.CreateIndex(
                name: "IX_PendingDevices_Status",
                table: "PendingDevices",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientLogs");

            migrationBuilder.DropTable(
                name: "ClientUnattends");

            migrationBuilder.DropTable(
                name: "Computers");

            migrationBuilder.DropTable(
                name: "DriverGroupFilters");

            migrationBuilder.DropTable(
                name: "DriverGroupPackages");

            migrationBuilder.DropTable(
                name: "DriverHardwareIds");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "MulticastClients");

            migrationBuilder.DropTable(
                name: "PendingDevices");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "DriverGroups");

            migrationBuilder.DropTable(
                name: "DriverPackages");

            migrationBuilder.DropTable(
                name: "MulticastTransmissions");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "ImageGroups");
        }
    }
}
