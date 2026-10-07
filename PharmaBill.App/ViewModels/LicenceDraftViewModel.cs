using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Entities;

namespace PharmaBill.App.ViewModels;

public class LicenceDraftViewModel : ObservableObject
{
	private string _licenceType = "20";

	private string _licenceNumber = string.Empty;

	private DateTime? _issueDate;

	private DateTime? _expiryDate;

	private string? _documentPath;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LicenceType
	{
		get
		{
			return _licenceType;
		}
		[MemberNotNull("_licenceType")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_licenceType, value))
			{
				OnPropertyChanging(nameof(LicenceType));
				_licenceType = value;
				OnPropertyChanged(nameof(LicenceType));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LicenceNumber
	{
		get
		{
			return _licenceNumber;
		}
		[MemberNotNull("_licenceNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_licenceNumber, value))
			{
				OnPropertyChanging(nameof(LicenceNumber));
				_licenceNumber = value;
				OnPropertyChanged(nameof(LicenceNumber));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? IssueDate
	{
		get
		{
			return _issueDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_issueDate, value))
			{
				OnPropertyChanging(nameof(IssueDate));
				_issueDate = value;
				OnPropertyChanged(nameof(IssueDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? ExpiryDate
	{
		get
		{
			return _expiryDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_expiryDate, value))
			{
				OnPropertyChanging(nameof(ExpiryDate));
				_expiryDate = value;
				OnPropertyChanged(nameof(ExpiryDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? DocumentPath
	{
		get
		{
			return _documentPath;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_documentPath, value))
			{
				OnPropertyChanging(nameof(DocumentPath));
				_documentPath = value;
				OnPropertyChanged(nameof(DocumentPath));
			}
		}
	}

	public LicenceRecord ToEntity()
	{
		if (string.IsNullOrWhiteSpace(LicenceNumber))
		{
			throw new InvalidOperationException("Enter the licence number.");
		}
		if (!IssueDate.HasValue || !ExpiryDate.HasValue)
		{
			throw new InvalidOperationException("Enter both the licence issue date and expiry date.");
		}
		if (IssueDate.Value.Date > DateTime.Today)
		{
			throw new InvalidOperationException("The licence issue date cannot be in the future.");
		}
		if (ExpiryDate.HasValue && IssueDate.HasValue && ExpiryDate.Value.Date < IssueDate.Value.Date)
		{
			throw new InvalidOperationException("The licence expiry date cannot be earlier than its issue date.");
		}
		string documentPath = CopyDocument(DocumentPath);
		return new LicenceRecord
		{
			LicenceType = LicenceType.Trim(),
			LicenceNumber = LicenceNumber.Trim(),
			IssuedOn = (IssueDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(IssueDate.Value)) : ((DateOnly?)null)),
			ExpiresOn = (ExpiryDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(ExpiryDate.Value)) : ((DateOnly?)null)),
			DocumentPath = documentPath
		};
	}

	private static string? CopyDocument(string? sourcePath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			return null;
		}
		string text = Path.GetExtension(sourcePath).ToLowerInvariant();
		switch (text)
		{
		default:
			throw new InvalidOperationException("Licence copy must be a PDF or image file.");
		case ".pdf":
		case ".png":
		case ".jpg":
		case ".jpeg":
		case ".bmp":
		{
			string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "licences");
			Directory.CreateDirectory(text2);
			string text3 = Path.Combine(text2, $"{Guid.NewGuid():N}{text}");
			File.Copy(sourcePath, text3, overwrite: false);
			return text3;
		}
		}
	}
}
