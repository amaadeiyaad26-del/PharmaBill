using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using Xunit;

namespace PharmaBill.Tests;

// "Search data bank" on Add Medicine: answers from the shop's medicines and the built-in data bank only. There is no
// online or AI step, so a missing Gemini key can no longer turn up as the answer.
public sealed class SmartDrugLookupLocalFirstTests
{
    [Fact]
    public async Task Lookup_AnswersFromTheDataBank_AndSaysPlainlyWhenItIsNotThere()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        await SeedAsync(database);
        var lookup = new SmartDrugLookupService(Services(database).GetRequiredService<IServiceScopeFactory>());

        SmartDrugLookupOutcome found = await lookup.LookupAsync("corex");
        Assert.Equal("Corex DX Syrup", found.Suggestion!.BrandName);
        Assert.Equal(SmartDrugLookupPrompt.FilledNotice, found.Message);

        SmartDrugLookupOutcome missing = await lookup.LookupAsync("limcer");
        Assert.Null(missing.Suggestion);
        Assert.Equal("'limcer' is not in the medicine data bank. Check the spelling, or type the details from the pack or the supplier invoice.", missing.Message);
        Assert.DoesNotContain("Gemini", missing.Message);
        Assert.DoesNotContain("online", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddMedicineWindow_SearchDataBank_FillsTheForm_OrSaysItIsNotThere()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            await SeedAsync(database);
            var window = new QuickAddMedicineWindow(Services(database).GetRequiredService<IServiceScopeFactory>(), null, "corex");
            Ui.Prepare(window);
            window.Show();
            await Ui.SettleAsync();
            var brand = (TextBox)window.FindName("BrandBox");
            var generic = (TextBox)window.FindName("GenericBox");
            var maker = (TextBox)window.FindName("ManufacturerBox");
            var notice = (TextBlock)window.FindName("NoticeText");
            Button search = Ui.Button(window, "🔍 Search data bank");

            await Ui.PressAsync(search);
            await Ui.UntilAsync(() => notice.IsVisible && search.IsEnabled, "the data bank search to finish");
            Assert.Equal("Dextromethorphan (10mg)", generic.Text);
            Assert.Equal("Pfizer Ltd", maker.Text);
            Assert.Equal(SmartDrugLookupPrompt.FilledNotice, notice.Text);

            Ui.Type(brand, "limcer");
            await Ui.PressAsync(search);
            await Ui.UntilAsync(() => notice.Text.Contains("limcer") && search.IsEnabled, "the data bank search to finish");
            Assert.StartsWith("'limcer' is not in the medicine data bank.", notice.Text);
            Assert.Equal("🔍 Search data bank", search.Content);
        });
    }

    private static async Task SeedAsync(DatabaseTestContext database)
    {
        database.Context.CatalogMedicines.Add(new CatalogMedicine
        {
            SourceId = "1",
            Name = "Corex DX Syrup",
            Manufacturer = "Pfizer Ltd",
            ShortComposition1 = "Dextromethorphan (10mg)",
            CompositionKey = "dextromethorphan 10mg"
        });
        await database.Context.SaveChangesAsync();
    }

    private static ServiceProvider Services(DatabaseTestContext database) => new ServiceCollection()
        .AddSingleton(database.Context)
        .AddSingleton<ISmartDrugLookupService>(sp => new SmartDrugLookupService(sp.GetRequiredService<IServiceScopeFactory>()))
        .BuildServiceProvider();
}
