using System;
using System.Collections.Generic;
using System.Linq;

namespace PharmaBill.Data.Services;

public static class DrugRecordsCalculator
{
	public static (IReadOnlyList<DrugRecordsRow> Rows, DrugRecordsSummary Summary) Calculate(IEnumerable<DrugRecordsMovement> source, decimal openingBalance, decimal currentStock, string recordType)
	{
		DrugRecordsMovement[] array = source.OrderBy((DrugRecordsMovement item) => item.AtUtc).ThenBy((DrugRecordsMovement item) => item.DocumentNo, StringComparer.Ordinal).ThenBy((DrugRecordsMovement item) => item.BatchNo, StringComparer.Ordinal)
			.ToArray();
		List<DrugRecordsRow> list = new List<DrugRecordsRow>();
		decimal runningBalance = openingBalance;
		DrugRecordsMovement[] array2 = array;
		foreach (DrugRecordsMovement drugRecordsMovement in array2)
		{
			runningBalance += drugRecordsMovement.BalanceChange ?? drugRecordsMovement.QuantityChange;
			bool flag = recordType != "Both";
			if (flag)
			{
				bool flag2 = recordType == "Sale";
				if (flag2)
				{
					string recordType2 = drugRecordsMovement.RecordType;
					bool flag3 = ((recordType2 == "Sale" || recordType2 == "Sale return") ? true : false);
					flag2 = flag3;
				}
				flag = !flag2;
			}
			bool flag4 = flag;
			if (flag4)
			{
				bool flag2 = recordType == "Purchase";
				if (flag2)
				{
					string recordType2 = drugRecordsMovement.RecordType;
					bool flag3 = ((recordType2 == "Purchase" || recordType2 == "Purchase return") ? true : false);
					flag2 = flag3;
				}
				flag4 = !flag2;
			}
			if (!flag4)
			{
				list.Add(new DrugRecordsRow(drugRecordsMovement.AtUtc, drugRecordsMovement.RecordType, drugRecordsMovement.DocumentNo, drugRecordsMovement.PatientName, drugRecordsMovement.Address, drugRecordsMovement.Phone, drugRecordsMovement.DoctorName, drugRecordsMovement.DoctorRegistrationNo, drugRecordsMovement.BatchNo, drugRecordsMovement.QuantityChange, runningBalance));
			}
		}
		decimal purchased = array.Where((DrugRecordsMovement item) =>
		{
			string recordType3 = item.RecordType;
			return (recordType3 == "Purchase" || recordType3 == "Purchase return") ? true : false;
		}).Sum((DrugRecordsMovement item) => item.QuantityChange);
		decimal sold = -array.Where((DrugRecordsMovement item) =>
		{
			string recordType3 = item.RecordType;
			return (recordType3 == "Sale" || recordType3 == "Sale return") ? true : false;
		}).Sum((DrugRecordsMovement item) => item.SalesQuantityChange ?? item.QuantityChange);
		decimal num2 = openingBalance + array.Sum((DrugRecordsMovement item) => item.BalanceChange ?? item.QuantityChange);
		int uniquePatients = (from item in array.Where((DrugRecordsMovement item) =>
			{
				string recordType3 = item.RecordType;
				return (recordType3 == "Sale" || recordType3 == "Sale return") ? true : false;
			})
			select item.PartyId?.ToString("D") ?? (item.PatientName + "\u001f" + item.Phone) into item
			where item != "\u001f"
			select item).Distinct(StringComparer.OrdinalIgnoreCase).Count();
		return (Rows: list, Summary: new DrugRecordsSummary(openingBalance, purchased, sold, num2, currentStock, uniquePatients, num2 == currentStock));
	}
}
