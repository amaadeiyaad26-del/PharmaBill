using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegisterRecordedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordedByName",
                table: "ScheduleRegisterEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordedBySource",
                table: "ScheduleRegisterEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByUserId",
                table: "ScheduleRegisterEntries",
                type: "TEXT",
                nullable: true);

            // Entries saved before this column existed are legal records: their own fields are never changed.
            // Who made them is filled in only where it can be traced exactly, and marked with how it was traced;
            // every other entry stays NULL and reads "Not recorded".

            // 1. A sale's entry: the bill records who billed it.
            migrationBuilder.Sql("""
                UPDATE ScheduleRegisterEntries
                SET RecordedByUserId = (SELECT s.BilledByUserId FROM Sales s WHERE s.Id = ScheduleRegisterEntries.SaleId),
                    RecordedByName = (SELECT COALESCE(NULLIF(u.DisplayName, ''), u.UserName)
                                      FROM Sales s JOIN AppUsers u ON u.Id = s.BilledByUserId
                                      WHERE s.Id = ScheduleRegisterEntries.SaleId),
                    RecordedBySource = 'FromBill'
                WHERE RecordedBySource IS NULL
                  AND SaleId IS NOT NULL
                  AND EXISTS (SELECT 1 FROM Sales s JOIN AppUsers u ON u.Id = s.BilledByUserId
                              WHERE s.Id = ScheduleRegisterEntries.SaleId);
                """);

            // 2. Any other entry: every row written by one save shares its HLC stamp and device, and each path
            //    that writes the register also writes an audit-log row with the acting user in that same save.
            //    Only a save whose audit rows name exactly one known user counts.
            migrationBuilder.Sql("""
                UPDATE ScheduleRegisterEntries
                SET RecordedByUserId = (SELECT a.UserId FROM AuditLogs a
                                        WHERE a.HlcStamp = ScheduleRegisterEntries.HlcStamp
                                          AND a.DeviceId = ScheduleRegisterEntries.DeviceId
                                          AND a.UserId IS NOT NULL
                                        LIMIT 1),
                    RecordedByName = (SELECT COALESCE(NULLIF(u.DisplayName, ''), u.UserName)
                                      FROM AuditLogs a JOIN AppUsers u ON u.Id = a.UserId
                                      WHERE a.HlcStamp = ScheduleRegisterEntries.HlcStamp
                                        AND a.DeviceId = ScheduleRegisterEntries.DeviceId
                                      LIMIT 1),
                    RecordedBySource = 'FromAuditLog'
                WHERE RecordedBySource IS NULL
                  AND HlcStamp <> ''
                  AND (SELECT COUNT(DISTINCT a.UserId) FROM AuditLogs a
                       WHERE a.HlcStamp = ScheduleRegisterEntries.HlcStamp
                         AND a.DeviceId = ScheduleRegisterEntries.DeviceId
                         AND a.UserId IS NOT NULL) = 1
                  AND EXISTS (SELECT 1 FROM AuditLogs a JOIN AppUsers u ON u.Id = a.UserId
                              WHERE a.HlcStamp = ScheduleRegisterEntries.HlcStamp
                                AND a.DeviceId = ScheduleRegisterEntries.DeviceId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordedByName",
                table: "ScheduleRegisterEntries");

            migrationBuilder.DropColumn(
                name: "RecordedBySource",
                table: "ScheduleRegisterEntries");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "ScheduleRegisterEntries");
        }
    }
}
