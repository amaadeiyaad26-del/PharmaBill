using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClosedXML.Excel;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using Xunit;

namespace PharmaBill.Tests;

public sealed class WholesaleCustomerTests
{
    [Fact]
    public void CustomerLicenceExpiryAlerts_UseThirtyAndSixtyDayBoundaries()
    {
        var today = new DateOnly(2026, 10, 3);

        Assert.Equal(
            CustomerLicenceExpiryAlert.Expired,
            CustomerLicenceExpiryAlerts.Evaluate(today.AddDays(-1), today));
        Assert.Equal(
            CustomerLicenceExpiryAlert.ExpiringWithin30Days,
            CustomerLicenceExpiryAlerts.Evaluate(today.AddDays(30), today));
        Assert.Equal(
            CustomerLicenceExpiryAlert.ExpiringWithin60Days,
            CustomerLicenceExpiryAlerts.Evaluate(today.AddDays(31), today));
        Assert.Equal(
            CustomerLicenceExpiryAlert.ExpiringWithin60Days,
            CustomerLicenceExpiryAlerts.Evaluate(today.AddDays(60), today));
        Assert.Equal(
            CustomerLicenceExpiryAlert.None,
            CustomerLicenceExpiryAlerts.Evaluate(today.AddDays(61), today));
        Assert.Equal(
            CustomerLicenceExpiryAlert.None,
            CustomerLicenceExpiryAlerts.Evaluate(null, today));
    }

    [Fact]
    public async Task WholesaleCustomerService_SavesMultipleLicencesAndVerificationFields()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var profile = new PharmacyProfile { Name = "Test pharmacy", WholesaleLicenceTypesJson = "[\"WHOLESALE-A\"]" };
        var owner = new AppUser
        {
            DisplayName = "Owner",
            UserName = "customer-owner",
            PasswordHash = "test",
            Role = UserRole.Owner
        };
        database.Context.AddRange(profile, owner);
        await database.Context.SaveChangesAsync();

        var customer = new Customer
        {
            Name = "North Distributor",
            BuyerType = "Distributor",
            Gstin = "29ABCDE1234F1Z5",
            CreditLimit = 25000m,
            CreditDays = 30,
            PriceCategory = "A",
            Route = "North",
            Salesman = "A. Sharma",
            OpeningBalance = 1500m,
            State = "Karnataka",
            StateDrugControlPortalUrl = "https://state.example/portal",
            VerifiedBy = "Owner",
            VerifiedOnUtc = DateTime.UtcNow,
            VerificationMethod = "Document checked"
        };
        var licences = new[]
        {
            new CustomerLicence
            {
                LicenceType = "WHOLESALE-A",
                LicenceNumber = "DL-W-101",
                IssuedOn = new DateOnly(2025, 1, 1),
                ExpiresOn = new DateOnly(2027, 1, 1),
                IssuingAuthority = "State Drug Control",
                DocumentPath = "licence.pdf"
            },
            new CustomerLicence
            {
                LicenceType = "20",
                LicenceNumber = "DL-20-101",
                IssuedOn = new DateOnly(2025, 1, 1),
                ExpiresOn = new DateOnly(2027, 1, 1)
            }
        };
        var service = new WholesaleCustomerService(new UnitOfWork(database.Context));

        await service.SaveAsync(customer, licences, ["WHOLESALE-A", "WHOLESALE-B"], owner.Id);

        var stored = await database.Context.Customers.SingleAsync();
        Assert.Equal("29ABCDE1234F1Z5", stored.Gstin);
        Assert.Equal(30, stored.CreditDays);
        Assert.Equal(1500m, stored.OpeningBalance);
        Assert.Equal("Document checked", stored.VerificationMethod);
        Assert.Equal(2, await database.Context.CustomerLicences.CountAsync());
        Assert.Equal(
            ["20", "21", "WHOLESALE-A", "WHOLESALE-B"],
            await service.GetLicenceTypesAsync());
        Assert.Contains(
            await database.Context.AuditLogs.ToListAsync(),
            log => log.Action == "WholesaleCustomerCreated" && log.EntityId == customer.Id);
    }

    [Fact]
    public async Task WholesaleCustomerService_BlocksDuplicateGstinAndLicenceNumbers()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var profile = new PharmacyProfile { Name = "Test pharmacy" };
        var owner = new AppUser
        {
            DisplayName = "Owner",
            UserName = "duplicate-owner",
            PasswordHash = "test",
            Role = UserRole.Owner
        };
        var customer = new Customer { Name = "Existing buyer", BuyerType = "Distributor", Gstin = "29ABCDE1234F1Z5" };
        database.Context.AddRange(profile, owner, customer);
        database.Context.CustomerLicences.Add(new CustomerLicence
        {
            CustomerId = customer.Id,
            LicenceType = "WHOLESALE-A",
            LicenceNumber = "DL 101",
            IssuedOn = new DateOnly(2025, 1, 1),
            ExpiresOn = new DateOnly(2027, 1, 1)
        });
        await database.Context.SaveChangesAsync();
        var service = new WholesaleCustomerService(new UnitOfWork(database.Context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
            new Customer { Name = "Duplicate GST buyer", BuyerType = "Distributor", Gstin = "29-ABCDE-1234F1Z5" },
            [],
            [],
            owner.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
            new Customer { Name = "Duplicate licence buyer", BuyerType = "Distributor", Gstin = "29OTHER1234F1Z5" },
            [new CustomerLicence
            {
                LicenceType = "WHOLESALE-A",
                LicenceNumber = "dl-101",
                IssuedOn = new DateOnly(2025, 1, 1),
                ExpiresOn = new DateOnly(2027, 1, 1)
            }],
            [],
            owner.Id));

        Assert.Single(await database.Context.Customers.ToListAsync());
    }

    [Fact]
    public async Task WholesaleCustomerService_ImportsValidCustomersAndSkipsGstinOrLicenceDuplicates()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var profile = new PharmacyProfile { Name = "Test pharmacy" };
        var owner = new AppUser
        {
            DisplayName = "Owner",
            UserName = "import-owner",
            PasswordHash = "test",
            Role = UserRole.Owner
        };
        var existing = new Customer { Name = "Existing buyer", BuyerType = "Distributor", Gstin = "GST-EXISTING" };
        database.Context.AddRange(profile, owner, existing);
        database.Context.CustomerLicences.Add(new CustomerLicence
        {
            CustomerId = existing.Id,
            LicenceType = "WHOLESALE-A",
            LicenceNumber = "DL-EXISTING"
        });
        await database.Context.SaveChangesAsync();

        var imports = new[]
        {
            new WholesaleCustomerImportRecord(
                new Customer { Name = "New buyer", BuyerType = "Distributor", Gstin = "GST-NEW" },
                [new CustomerLicence
                {
                    LicenceType = "WHOLESALE-B",
                    LicenceNumber = "DL-NEW",
                    IssuedOn = new DateOnly(2025, 1, 1),
                    ExpiresOn = new DateOnly(2027, 1, 1)
                }]),
            new WholesaleCustomerImportRecord(
                new Customer { Name = "GST duplicate", BuyerType = "Distributor", Gstin = "GST EXISTING" },
                [new CustomerLicence
                {
                    LicenceType = "WHOLESALE-A",
                    LicenceNumber = "DL-OTHER",
                    IssuedOn = new DateOnly(2025, 1, 1),
                    ExpiresOn = new DateOnly(2027, 1, 1)
                }]),
            new WholesaleCustomerImportRecord(
                new Customer { Name = "Licence duplicate", BuyerType = "Distributor", Gstin = "GST-OTHER" },
                [new CustomerLicence
                {
                    LicenceType = "WHOLESALE-A",
                    LicenceNumber = "dl existing",
                    IssuedOn = new DateOnly(2025, 1, 1),
                    ExpiresOn = new DateOnly(2027, 1, 1)
                }])
        };
        var service = new WholesaleCustomerService(new UnitOfWork(database.Context));

        var result = await service.ImportAsync(imports, [], owner.Id);

        Assert.Equal(1, result.Imported);
        Assert.Equal(2, result.SkippedDuplicates);
        Assert.Empty(result.Errors);
        Assert.Equal(2, await database.Context.Customers.CountAsync());
        Assert.Contains("WHOLESALE-B", await service.GetLicenceTypesAsync());
        Assert.Contains(
            await database.Context.AuditLogs.ToListAsync(),
            log => log.Action == "WholesaleCustomersImported");
    }

    [Fact]
    public async Task WholesaleCustomerService_ReportsBlockedStatusAndNearExpiry()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var customer = new Customer
        {
            Name = "Blocked buyer",
            BuyerType = "Distributor",
            IsActive = false
        };
        database.Context.Customers.Add(customer);
        database.Context.CustomerLicences.Add(new CustomerLicence
        {
            CustomerId = customer.Id,
            LicenceType = "WHOLESALE-A",
            LicenceNumber = "DL-NEAR",
            IssuedOn = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
        });
        await database.Context.SaveChangesAsync();

        var result = await new WholesaleCustomerService(new UnitOfWork(database.Context)).GetCustomersAsync();

        var buyer = Assert.Single(result);
        Assert.False(buyer.Customer.IsActive);
        Assert.Equal("Blocked", buyer.StatusText);
        Assert.Equal(CustomerLicenceExpiryAlert.ExpiringWithin30Days, buyer.ExpiryAlert);
    }

    [Fact]
    public async Task CustomerExcelImport_ReadsRepeatedBuyerRowsAndMultipleLicences()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.AddRange(
            new PharmacyProfile { Name = "Test pharmacy" },
            new AppUser
            {
                DisplayName = "Owner",
                UserName = "excel-owner",
                PasswordHash = "test",
                Role = UserRole.Owner
            });
        await database.Context.SaveChangesAsync();
        var owner = await database.Context.AppUsers.SingleAsync();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Customers");
                var headers = new[]
                {
                    "Customer Name", "Buyer Type", "GSTIN", "Phone", "Licence Type",
                    "Licence Number", "Issue Date", "Expiry Date", "Issuing Authority"
                };
                for (var column = 0; column < headers.Length; column++)
                {
                    sheet.Cell(1, column + 1).Value = headers[column];
                }

                AddRow(sheet, 2, "Excel buyer", "Distributor", "GST-EXCEL", "9990001111",
                    "WHOLESALE-A", "DL-EXCEL-1", "Authority A");
                AddRow(sheet, 3, "Excel buyer", "Distributor", "GST-EXCEL", "9990001111",
                    "WHOLESALE-B", "DL-EXCEL-2", "Authority B");
                workbook.SaveAs(path);
            }

            using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .AddSingleton<IUnitOfWork>(new UnitOfWork(database.Context))
                .AddScoped<WholesaleCustomerService>()
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(owner);
            var viewModel = new WholesaleCustomerPageViewModel(
                services.GetRequiredService<IServiceScopeFactory>(),
                new SelectedCustomerFilePicker(path),
                new PurchaseSpreadsheetReader(),
                session,
                new ConfirmationService());

            await viewModel.ImportExcelCommand.ExecuteAsync(null);

            Assert.Empty(viewModel.ErrorMessage);
            var customer = await database.Context.Customers.SingleAsync();
            Assert.Equal("Excel buyer", customer.Name);
            Assert.Equal("GST-EXCEL", customer.Gstin);
            Assert.Equal(2, await database.Context.CustomerLicences.CountAsync());
            Assert.Contains("Imported 1", viewModel.StatusMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AddRow(
        IXLWorksheet sheet,
        int row,
        string name,
        string buyerType,
        string gstin,
        string phone,
        string licenceType,
        string licenceNumber,
        string authority)
    {
        var values = new object[]
        {
            name,
            buyerType,
            gstin,
            phone,
            licenceType,
            licenceNumber,
            DateTime.Today.AddYears(-1),
            DateTime.Today.AddYears(1),
            authority
        };
        for (var column = 0; column < values.Length; column++)
        {
            sheet.Cell(row, column + 1).Value = XLCellValue.FromObject(values[column]);
        }

        sheet.Cell(row, 7).Style.DateFormat.Format = "yyyy-MM-dd";
        sheet.Cell(row, 8).Style.DateFormat.Format = "yyyy-MM-dd";
    }

    private sealed class SelectedCustomerFilePicker(string path) : IFilePickerService
    {
        public string? PickLicenceDocument() => null;
        public string? PickCsvFile() => null;
        public string? PickPurchaseDocument() => null;
        public string? PickPurchaseSpreadsheet() => null;
        public string? PickCustomerExcel() => path;
        public string? PickPrescriptionDocument() => null;
        public IReadOnlyList<string> PickInvoiceDocuments() => Array.Empty<string>();
        public string? PickExportDestination(string extension, string suggestedName) => null;
        public string? PickFolder(string title) => null;
        public string? PickBackupFile() => null;
        public string? PickSyncPackage() => null;
    }
}
