using System.Net;
using System.Net.Http;
using System.Text;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using Xunit;

namespace PharmaBill.Tests;

public sealed class SmartDrugLookupTests
{
	[Fact]
	public void ParseSuggestionJson_ReadsPharmacyFieldsAndDefaultsTax()
	{
		const string json = """
			{"brandName":"Zyloquin 250","genericName":"Cefquinome","manufacturer":"Acme Labs","packSizeLabel":"Strip of 10","gstRate":5,"hsnCode":"3004","approximateMrp":48.5}
			""";

		SmartDrugSuggestion? suggestion = SmartDrugLookupService.ParseSuggestionJson(json, "Zyloquin");

		Assert.NotNull(suggestion);
		Assert.Equal("Zyloquin 250", suggestion.BrandName);
		Assert.Equal("Cefquinome", suggestion.GenericName);
		Assert.Equal("Acme Labs", suggestion.Manufacturer);
		Assert.Equal("Strip of 10", suggestion.PackSizeLabel);
		Assert.Equal(5m, suggestion.GstRate);
		Assert.Equal("3004", suggestion.HsnCode);
		Assert.Equal(48.5m, suggestion.ApproximateMrp);
	}

	[Fact]
	public void ParseSuggestionJson_UsesStandardSlabWhenTaxIsMissing()
	{
		SmartDrugSuggestion? suggestion = SmartDrugLookupService.ParseSuggestionJson("{\"brandName\":\"Local Tab\"}", "Local Tab");

		Assert.NotNull(suggestion);
		Assert.Equal(12m, suggestion.GstRate);
		Assert.Equal("3004", suggestion.HsnCode);
		Assert.Null(suggestion.ApproximateMrp);
		Assert.Null(suggestion.GenericName);
	}

	[Fact]
	public async Task TryLookupAsync_ReturnsNullWhenOfflineOrTimedOut()
	{
		SmartDrugLookupService offline = new SmartDrugLookupService(
			new HttpClient(new ThrowingHandler(new HttpRequestException("offline"))),
			() => "test-key");
		Assert.Null(await offline.TryLookupAsync("Zyloquin 250"));

		SmartDrugLookupService timedOut = new SmartDrugLookupService(
			new HttpClient(new ThrowingHandler(new TaskCanceledException("timeout"))),
			() => "test-key");
		Assert.Null(await timedOut.TryLookupAsync("Zyloquin 250"));

		SmartDrugLookupService missingKey = new SmartDrugLookupService(new HttpClient(new ThrowingHandler(new InvalidOperationException("should not send"))), () => null);
		Assert.Null(await missingKey.TryLookupAsync("Zyloquin 250"));
	}

	[Fact]
	public async Task TryLookupAsync_ParsesProviderJson()
	{
		const string body = """
			{"candidates":[{"content":{"parts":[{"text":"{\"brandName\":\"Zyloquin 250\",\"genericName\":\"Cefquinome\",\"manufacturer\":\"Acme Labs\",\"packSizeLabel\":\"Strip of 10\",\"gstRate\":12,\"hsnCode\":\"3004\",\"approximateMrp\":48}"}]}}]}
			""";
		SmartDrugLookupService service = new SmartDrugLookupService(
			new HttpClient(new FixedHandler(body)),
			() => "test-key");

		SmartDrugSuggestion? suggestion = await service.TryLookupAsync("Zyloquin");

		Assert.NotNull(suggestion);
		Assert.Equal("Zyloquin 250", suggestion.BrandName);
		Assert.Equal("Cefquinome", suggestion.GenericName);
		Assert.Equal("Strip of 10", suggestion.PackSizeLabel);
		Assert.Equal(12m, suggestion.GstRate);
	}

	private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromException<HttpResponseMessage>(error);
	}

	private sealed class FixedHandler(string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			});
	}
}
