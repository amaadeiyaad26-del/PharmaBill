using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PharmaBill.Core;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public static class InvoiceFlowDocumentFactory
{
	public static FlowDocument Create(RetailBillPrintData bill, DocumentOutputSettings settings)
	{
		FlowDocument flowDocument = new FlowDocument
		{
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 11.0,
			PagePadding = new Thickness(40.0),
			ColumnWidth = double.PositiveInfinity
		};
		Paragraph paragraph = new Paragraph
		{
			TextAlignment = TextAlignment.Center
		};
		if (settings.PrintShopLogo && !string.IsNullOrWhiteSpace(settings.LogoPath) && File.Exists(settings.LogoPath))
		{
			try
			{
				Image childUIElement = new Image
				{
					Source = new BitmapImage(new Uri(settings.LogoPath)),
					Height = 48.0,
					Stretch = Stretch.Uniform
				};
				paragraph.Inlines.Add(new InlineUIContainer(childUIElement));
				paragraph.Inlines.Add(new LineBreak());
			}
			catch
			{
			}
		}
		paragraph.Inlines.Add(new Run(bill.PharmacyName)
		{
			FontSize = 18.0,
			FontWeight = FontWeights.Bold
		});
		paragraph.Inlines.Add(new LineBreak());
		if (!string.IsNullOrWhiteSpace(bill.PharmacyAddress))
		{
			paragraph.Inlines.Add(new Run(bill.PharmacyAddress));
			paragraph.Inlines.Add(new LineBreak());
		}
		if (!string.IsNullOrWhiteSpace(bill.AddressLine2))
		{
			paragraph.Inlines.Add(new Run(bill.AddressLine2));
			paragraph.Inlines.Add(new LineBreak());
		}
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(bill.PharmacyPhone))
		{
			list.Add("Phone: " + bill.PharmacyPhone);
		}
		if (!string.IsNullOrWhiteSpace(bill.Gstin))
		{
			list.Add("GSTIN: " + bill.Gstin);
		}
		if (!string.IsNullOrWhiteSpace(bill.FssaiNumber))
		{
			list.Add("FSSAI: " + bill.FssaiNumber);
		}
		if (list.Count > 0)
		{
			paragraph.Inlines.Add(new Run(string.Join("  |  ", list)));
			paragraph.Inlines.Add(new LineBreak());
		}
		if (bill.LicenceNumbers.Count > 0)
		{
			paragraph.Inlines.Add(new Run("Drug Licence: " + string.Join(", ", bill.LicenceNumbers)));
		}
		flowDocument.Blocks.Add(paragraph);
		flowDocument.Blocks.Add(new Paragraph(new Run("TAX INVOICE")
		{
			FontSize = 14.0,
			FontWeight = FontWeights.Bold
		})
		{
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(0.0, 12.0, 0.0, 8.0)
		});
		Paragraph paragraph2 = new Paragraph();
		paragraph2.Inlines.Add(new Run("Invoice No: " + bill.InvoiceNo)
		{
			FontWeight = FontWeights.SemiBold
		});
		paragraph2.Inlines.Add(new LineBreak());
		paragraph2.Inlines.Add(new Run($"Date: {bill.SaleAtUtc.ToLocalTime():dd-MMM-yyyy HH:mm}"));
		paragraph2.Inlines.Add(new LineBreak());
		paragraph2.Inlines.Add(new Run("Buyer / Patient: " + bill.PatientName));
		if (settings.PrintCustomerPhone && !string.IsNullOrWhiteSpace(bill.PatientPhone))
		{
			paragraph2.Inlines.Add(new Run("    Phone: " + bill.PatientPhone));
		}
		if (!string.IsNullOrWhiteSpace(bill.PatientAddress))
		{
			paragraph2.Inlines.Add(new LineBreak());
			paragraph2.Inlines.Add(new Run("Address: " + bill.PatientAddress));
		}
		if (!string.IsNullOrWhiteSpace(bill.MrdNumber))
		{
			paragraph2.Inlines.Add(new LineBreak());
			paragraph2.Inlines.Add(new Run("MRD / IPD No: " + bill.MrdNumber));
		}
		if (settings.PrintDoctorName && !string.IsNullOrWhiteSpace(bill.PrescriberName))
		{
			paragraph2.Inlines.Add(new LineBreak());
			paragraph2.Inlines.Add(new Run("Doctor: " + bill.PrescriberName));
			if (!string.IsNullOrWhiteSpace(bill.PrescriberRegistrationNumber))
			{
				paragraph2.Inlines.Add(new Run("  Reg. No: " + bill.PrescriberRegistrationNumber));
			}
		}
		flowDocument.Blocks.Add(paragraph2);
		Table table = new Table
		{
			CellSpacing = 0.0
		};
		table.Columns.Add(new TableColumn
		{
			Width = new GridLength(2.6, GridUnitType.Star)
		});
		table.Columns.Add(new TableColumn
		{
			Width = new GridLength(0.9, GridUnitType.Star)
		});
		table.Columns.Add(new TableColumn
		{
			Width = new GridLength(0.9, GridUnitType.Star)
		});
		table.Columns.Add(new TableColumn
		{
			Width = new GridLength(0.7, GridUnitType.Star)
		});
		table.Columns.Add(new TableColumn
		{
			Width = new GridLength(0.8, GridUnitType.Star)
		});
		table.Columns.Add(new TableColumn
		{
			Width = new GridLength(0.8, GridUnitType.Star)
		});
		table.Columns.Add(new TableColumn
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		TableRowGroup tableRowGroup = new TableRowGroup();
		table.RowGroups.Add(tableRowGroup);
		tableRowGroup.Rows.Add(HeaderRow("Medicine / Batch / Exp", "HSN", "Qty", "Rate", "CGST", "SGST", "Amount"));
		foreach (RetailBillPrintLine item in bill.Items)
		{
			decimal num = decimal.Round(item.TaxAmount / 2m, 2, MidpointRounding.AwayFromZero);
			tableRowGroup.Rows.Add(BodyRow($"{item.DrugName}\nBatch {item.BatchNo}  Exp {item.ExpiryDate?.ToString("MM/yy") ?? "—"}", item.HsnCode ?? "—", item.Quantity.ToString("0.##"), item.UnitPrice.ToString("N2"), num.ToString("N2"), (item.TaxAmount - num).ToString("N2"), item.LineTotal.ToString("N2")));
		}
		flowDocument.Blocks.Add(table);
		Paragraph paragraph3 = new Paragraph
		{
			TextAlignment = TextAlignment.Right,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		paragraph3.Inlines.Add(new Run("Taxable value: " + MoneyFormat.Rupees(bill.Subtotal)));
		paragraph3.Inlines.Add(new LineBreak());
		paragraph3.Inlines.Add(new Run("CGST: " + MoneyFormat.Rupees(bill.CgstAmount) + "    SGST: " + MoneyFormat.Rupees(bill.SgstAmount)));
		paragraph3.Inlines.Add(new LineBreak());
		paragraph3.Inlines.Add(new Run("Discount: " + MoneyFormat.Rupees(bill.DiscountAmount)));
		paragraph3.Inlines.Add(new LineBreak());
		paragraph3.Inlines.Add(new Run("Grand Total: " + MoneyFormat.Rupees(bill.TotalAmount))
		{
			FontWeight = FontWeights.Bold,
			FontSize = 13.0
		});
		paragraph3.Inlines.Add(new LineBreak());
		paragraph3.Inlines.Add(new Run("Amount in words: " + IndianNumberWords.Convert(bill.TotalAmount)));
		flowDocument.Blocks.Add(paragraph3);
		if (settings.PrintTermsDisclaimer && !string.IsNullOrWhiteSpace(bill.TermsText))
		{
			flowDocument.Blocks.Add(new Paragraph(new Run("Terms & conditions: " + bill.TermsText))
			{
				FontSize = 9.0,
				Margin = new Thickness(0.0, 14.0, 0.0, 0.0),
				Foreground = Brushes.DimGray
			});
		}
		Paragraph paragraph4 = new Paragraph
		{
			Margin = new Thickness(0.0, 28.0, 0.0, 0.0)
		};
		paragraph4.Inlines.Add(new Run("For " + bill.PharmacyName));
		paragraph4.Inlines.Add(new LineBreak());
		paragraph4.Inlines.Add(new LineBreak());
		paragraph4.Inlines.Add(new Run("______________________________"));
		paragraph4.Inlines.Add(new LineBreak());
		paragraph4.Inlines.Add(new Run("Authorized Signatory"));
		if (!string.IsNullOrWhiteSpace(bill.PharmacistName))
		{
			paragraph4.Inlines.Add(new LineBreak());
			paragraph4.Inlines.Add(new Run(bill.PharmacistName));
		}
		if (!string.IsNullOrWhiteSpace(bill.PharmacistRegistrationNumber))
		{
			paragraph4.Inlines.Add(new LineBreak());
			paragraph4.Inlines.Add(new Run("Reg. No: " + bill.PharmacistRegistrationNumber));
		}
		flowDocument.Blocks.Add(paragraph4);
		if (!string.IsNullOrWhiteSpace(settings.FooterText))
		{
			flowDocument.Blocks.Add(new Paragraph(new Run(settings.FooterText))
			{
				FontSize = 9.0,
				TextAlignment = TextAlignment.Center,
				Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
			});
		}
		return flowDocument;
	}

	private static TableRow HeaderRow(params string[] cells)
	{
		TableRow tableRow = new TableRow();
		foreach (string text in cells)
		{
			tableRow.Cells.Add(new TableCell(new Paragraph(new Run(text)
			{
				FontWeight = FontWeights.Bold
			}))
			{
				BorderBrush = Brushes.Gray,
				BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
				Padding = new Thickness(4.0)
			});
		}
		return tableRow;
	}

	private static TableRow BodyRow(params string[] cells)
	{
		TableRow tableRow = new TableRow();
		foreach (string text in cells)
		{
			tableRow.Cells.Add(new TableCell(new Paragraph(new Run(text)))
			{
				BorderBrush = Brushes.LightGray,
				BorderThickness = new Thickness(0.0, 0.0, 0.0, 0.5),
				Padding = new Thickness(4.0)
			});
		}
		return tableRow;
	}
}
