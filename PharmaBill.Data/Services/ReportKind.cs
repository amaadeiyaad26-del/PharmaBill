namespace PharmaBill.Data.Services;

public enum ReportKind
{
	Sales = 0,
	Purchases = 1,
	Profit = 2,
	GstSummary = 3,
	TopSellingDrugs = 4,
	Expiry = 5,
	LowStock = 6,
	SupplierWise = 7,
	SalesSummary = Sales,
	CustomerPartyWise = 100,
	PurchaserSupplierWise = 101,
	BatchWise = 102,
	BrandProductWise = 103,
	ManufacturerWise = 104,
	DailyMis = 200,
	AbcAnalysis = 201,
	SaleBookDaily = 202,
	SaleBookMonthly = 203,
	PrescriberWise = 204
}
