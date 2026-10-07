using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using PharmaBill.Core.Entities;

namespace PharmaBill.App.ViewModels;

public class LicenceDraftViewModel : ObservableObject
{
	[ObservableProperty]
	private string _licenceType = "20";

	[ObservableProperty]
	private string _licenceNumber = string.Empty;

	[ObservableProperty]
	private DateTime? _issueDate;

	[ObservableProperty]
	private DateTime? _expiryDate;

	[ObservableProperty]
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LicenceType);
				_licenceType = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LicenceType);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LicenceNumber);
				_licenceNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LicenceNumber);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IssueDate);
				_issueDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IssueDate);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ExpiryDate);
				_expiryDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ExpiryDate);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DocumentPath);
				_documentPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DocumentPath);
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
