using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Data.Services.Reconciliation;

namespace PharmaBill.App.ViewModels;

public class BankStatementMatchRow : ObservableObject
{
	private bool _approve;

	/// <summary>Raised when the user picks a chemist for this line in the dropdown.</summary>
	public event Action<BankStatementMatchRow, Guid>? ChemistChosen;

	public required ReconciliationMatch Match { get; set; }

	/// <summary>The chemist this line is linked to. Picking one in the dropdown re-matches the line by hand.</summary>
	public Guid? ChemistId
	{
		get => Match.CustomerId;
		set
		{
			if (value.HasValue && value != Match.CustomerId)
			{
				ChemistChosen?.Invoke(this, value.Value);
			}
		}
	}

	public void SetMatch(ReconciliationMatch match)
	{
		Match = match;
		OnPropertyChanged(string.Empty);
	}

	public string BankDate => Match.BankLine.Date.ToString("dd-MMM-yyyy");

	public string Reference => Match.BankLine.ReferenceOrUtr ?? string.Empty;

	public string Narration => Match.BankLine.Narration;

	public decimal DepositAmount => Match.BankLine.DepositAmount;

	public string CustomerName => Match.CustomerName ?? "—";

	public bool IsUnmatched => Match.MatchedDocuments.Count == 0;

	public string MatchedDocs
	{
		get
		{
			if (Match.MatchedDocuments.Count != 0)
			{
				return string.Join(", ", Match.MatchedDocuments.Select((MatchedDocument doc) => $"{doc.DocumentNo} (₹{doc.BalanceAmount:0.00})"));
			}
			return "—";
		}
	}

	public string Confidence => $"{Match.ConfidencePercent:0.#}% ({Match.Confidence})";

	public string Reason => Match.MatchReason;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool Approve
	{
		get
		{
			return _approve;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_approve, value))
			{
				OnPropertyChanging(nameof(Approve));
				_approve = value;
				OnPropertyChanged(nameof(Approve));
			}
		}
	}
}
