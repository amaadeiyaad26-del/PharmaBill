using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using PharmaBill.Core.Entities;

namespace PharmaBill.App.ViewModels;

public class CustomerLicenceDraft : ObservableObject
{
	[ObservableProperty]
	private Guid _id = Guid.NewGuid();

	[ObservableProperty]
	private string _licenceType = string.Empty;

	[ObservableProperty]
	private string _licenceNumber = string.Empty;

	[ObservableProperty]
	private DateTime? _issuedOn;

	[ObservableProperty]
	private DateTime? _expiresOn;

	[ObservableProperty]
	private string _issuingAuthority = string.Empty;

	[ObservableProperty]
	private string _documentPath = string.Empty;

	[ObservableProperty]
	private string _authorisation = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Guid Id
	{
		get
		{
			return _id;
		}
		set
		{
			if (!EqualityComparer<Guid>.Default.Equals(_id, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Id);
				_id = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Id);
			}
		}
	}

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
	public DateTime? IssuedOn
	{
		get
		{
			return _issuedOn;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_issuedOn, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IssuedOn);
				_issuedOn = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IssuedOn);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? ExpiresOn
	{
		get
		{
			return _expiresOn;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_expiresOn, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ExpiresOn);
				_expiresOn = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ExpiresOn);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string IssuingAuthority
	{
		get
		{
			return _issuingAuthority;
		}
		[MemberNotNull("_issuingAuthority")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_issuingAuthority, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IssuingAuthority);
				_issuingAuthority = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IssuingAuthority);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DocumentPath
	{
		get
		{
			return _documentPath;
		}
		[MemberNotNull("_documentPath")]
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Authorisation
	{
		get
		{
			return _authorisation;
		}
		[MemberNotNull("_authorisation")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_authorisation, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Authorisation);
				_authorisation = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Authorisation);
			}
		}
	}

	public CustomerLicence ToEntity()
	{
		if (string.IsNullOrWhiteSpace(LicenceType) || string.IsNullOrWhiteSpace(LicenceNumber))
		{
			throw new ArgumentException("Each licence needs a type and licence number.");
		}
		if (IssuedOn.HasValue && ExpiresOn.HasValue && ExpiresOn.Value.Date < IssuedOn.Value.Date)
		{
			throw new ArgumentException("Licence " + LicenceNumber + ": expiry cannot be earlier than issue date.");
		}
		return new CustomerLicence
		{
			Id = Id,
			LicenceType = LicenceType.Trim(),
			LicenceNumber = LicenceNumber.Trim(),
			IssuedOn = (IssuedOn.HasValue ? new DateOnly?(DateOnly.FromDateTime(IssuedOn.Value)) : ((DateOnly?)null)),
			ExpiresOn = (ExpiresOn.HasValue ? new DateOnly?(DateOnly.FromDateTime(ExpiresOn.Value)) : ((DateOnly?)null)),
			IssuingAuthority = (string.IsNullOrWhiteSpace(IssuingAuthority) ? null : IssuingAuthority.Trim()),
			DocumentPath = (string.IsNullOrWhiteSpace(DocumentPath) ? null : DocumentPath),
			Authorisation = (string.IsNullOrWhiteSpace(Authorisation) ? null : Authorisation.Trim())
		};
	}

	public static CustomerLicenceDraft FromEntity(CustomerLicence licence)
	{
		return new CustomerLicenceDraft
		{
			Id = licence.Id,
			LicenceType = licence.LicenceType,
			LicenceNumber = licence.LicenceNumber,
			IssuedOn = licence.IssuedOn?.ToDateTime(TimeOnly.MinValue),
			ExpiresOn = licence.ExpiresOn?.ToDateTime(TimeOnly.MinValue),
			IssuingAuthority = (licence.IssuingAuthority ?? string.Empty),
			DocumentPath = (licence.DocumentPath ?? string.Empty),
			Authorisation = (licence.Authorisation ?? string.Empty)
		};
	}
}
