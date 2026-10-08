using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class AccessControlTests
{
    [Fact]
    public void TrialClock_CountsSevenDaysFromTheEarliestStoredStart()
    {
        var firstStart = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var evaluation = TrialClock.Evaluate(
            firstStart.AddDays(2),
            [firstStart.AddDays(1), firstStart],
            firstStart);

        Assert.Equal(firstStart, evaluation.TrialStartedAtUtc);
        Assert.True(evaluation.IsTrialActive);
        Assert.Equal(TimeSpan.FromDays(5), evaluation.Remaining);
    }

    [Fact]
    public void TrialClock_ExpiresWhenClockMovesBackwards()
    {
        var lastObserved = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var evaluation = TrialClock.Evaluate(
            lastObserved.AddMinutes(-1),
            [lastObserved.AddDays(-1)],
            lastObserved);

        Assert.True(evaluation.ClockMovedBackwards);
        Assert.False(evaluation.IsTrialActive);
        Assert.Equal(TimeSpan.Zero, evaluation.Remaining);
    }

    [Fact]
    public void TrialClock_ExpiresAtTheSevenDayBoundary()
    {
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var evaluation = TrialClock.Evaluate(start.AddDays(7), [start], start.AddDays(6));

        Assert.False(evaluation.IsTrialActive);
        Assert.Equal(TimeSpan.Zero, evaluation.Remaining);
    }

    [Theory]
    [InlineData(ProtectedOperation.View, true)]
    [InlineData(ProtectedOperation.Search, true)]
    [InlineData(ProtectedOperation.Print, true)]
    [InlineData(ProtectedOperation.Export, true)]
    [InlineData(ProtectedOperation.Backup, true)]
    [InlineData(ProtectedOperation.CreateBill, false)]
    [InlineData(ProtectedOperation.EnterStock, false)]
    [InlineData(ProtectedOperation.ProcessReturn, false)]
    public async Task AuthorizationService_ReadOnlyAllowsOnlyNonMutatingOperations(
        ProtectedOperation operation,
        bool expected)
    {
        var service = new AuthorizationService(new FixedEntitlementService(readOnly: true));

        var allowed = await service.CanPerformAsync(
            UserRole.Owner,
            operation switch
            {
                ProtectedOperation.View => AppPermission.View,
                ProtectedOperation.Search => AppPermission.Search,
                ProtectedOperation.Print => AppPermission.Print,
                ProtectedOperation.Export => AppPermission.Export,
                ProtectedOperation.Backup => AppPermission.Backup,
                ProtectedOperation.CreateBill => AppPermission.CreateBill,
                ProtectedOperation.EnterStock => AppPermission.EnterStock,
                _ => AppPermission.ProcessReturn
            },
            operation);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public void PermissionMatrix_UsesConfiguredRolePermissions()
    {
        Assert.True(PermissionMatrix.Allows(UserRole.BillingClerk, AppPermission.CreateBill));
        Assert.False(PermissionMatrix.Allows(UserRole.BillingClerk, AppPermission.ChangeBusinessMode));
        Assert.True(PermissionMatrix.Allows(UserRole.Accountant, AppPermission.Backup));
        Assert.False(PermissionMatrix.Allows(UserRole.Accountant, AppPermission.EnterStock));
    }

    [Fact]
    public void ModeGuard_RequiresModeSpecificUnexpiredLicenceTypes()
    {
        var today = new DateOnly(2026, 10, 3);
        var licenses = new[]
        {
            new LicenceRecord { LicenceType = "20", LicenceNumber = "R-20", ExpiresOn = today.AddDays(20) },
            new LicenceRecord { LicenceType = "W-A", LicenceNumber = "W-A-01", ExpiresOn = today.AddDays(20) }
        };

        Assert.Empty(ModeGuard.GetMissingLicenceTypes(BusinessMode.Both, licenses, today));
        Assert.Single(ModeGuard.GetMissingLicenceTypes(
            BusinessMode.Wholesaler,
            [licenses[0]],
            today));
        Assert.Single(ModeGuard.GetMissingLicenceTypes(
            BusinessMode.Retail,
            [new LicenceRecord
            {
                LicenceType = licenses[0].LicenceType,
                LicenceNumber = licenses[0].LicenceNumber,
                ExpiresOn = today.AddDays(-1)
            }],
            today));
        Assert.Empty(ModeGuard.GetMissingLicenceTypes(
            BusinessMode.Retail,
            [new LicenceRecord
            {
                LicenceType = "20",
                LicenceNumber = "R-20-no-dates",
                ExpiresOn = null
            }],
            today));
        Assert.Empty(ModeGuard.GetMissingLicenceTypes(
            BusinessMode.Wholesaler,
            [new LicenceRecord
            {
                LicenceType = "20B",
                LicenceNumber = "W-20B-no-dates",
                ExpiresOn = null
            }],
            today));
    }

    [Fact]
    public void PasswordHasher_HashesAndVerifiesUsingPbkdf2()
    {
        var hash = PasswordHasher.Hash("pharmacy-pin");

        Assert.StartsWith("PBKDF2-SHA256$", hash);
        Assert.True(PasswordHasher.Verify("pharmacy-pin", hash));
        Assert.False(PasswordHasher.Verify("incorrect", hash));
    }

    private sealed class FixedEntitlementService(bool readOnly) : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(readOnly ? EntitlementStatus.EXPIRED : EntitlementStatus.TRIAL);

        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(readOnly);

        public Task<bool> CanPerformAsync(
            ProtectedOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(!readOnly || PermissionMatrix.AllowsInReadOnlyMode(operation switch
            {
                ProtectedOperation.View => AppPermission.View,
                ProtectedOperation.Search => AppPermission.Search,
                ProtectedOperation.Print => AppPermission.Print,
                ProtectedOperation.Export => AppPermission.Export,
                ProtectedOperation.Backup => AppPermission.Backup,
                _ => AppPermission.CreateBill
            }));
    }
}
