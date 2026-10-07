using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations;

/// <inheritdoc />
public partial class AppUserSocialAuth : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Live databases from the Oct 6 installer already include AuthProvider /
        // ProviderSubjectId. Adding them again fails with "duplicate column name".
        // Keep this migration as a model/history marker only; column ensure is
        // handled at startup in DatabaseInitializationHostedService.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op: do not drop columns that may still be required by installed builds.
    }
}
