using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.ViewModels;

// Wholesale Suppliers: the distributors and wholesalers the pharmacy buys from.
// Reads and writes the same Supplier table that purchase bills use.
public sealed partial class SupplierDirectoryPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private Guid? _editingId;

	private Supplier? _selectedSupplier;

	private string _name = string.Empty;

	private string _phone = string.Empty;

	private string _email = string.Empty;

	private string _address = string.Empty;

	private string _gstin = string.Empty;

	private string _drugLicenceNumber = string.Empty;

	private bool _isActive = true;

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	public SupplierDirectoryPageViewModel(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
		SaveCommand = new AsyncRelayCommand(SaveAsync);
		NewCommand = new RelayCommand(NewSupplier);
	}

	public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

	public IAsyncRelayCommand SaveCommand { get; }

	public IRelayCommand NewCommand { get; }

	public Supplier? SelectedSupplier
	{
		get
		{
			return _selectedSupplier;
		}
		set
		{
			if (SetProperty(ref _selectedSupplier, value) && value != null)
			{
				ShowSupplier(value);
			}
		}
	}

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			SetProperty(ref _name, value);
		}
	}

	public string Phone
	{
		get
		{
			return _phone;
		}
		set
		{
			SetProperty(ref _phone, value);
		}
	}

	public string Email
	{
		get
		{
			return _email;
		}
		set
		{
			SetProperty(ref _email, value);
		}
	}

	public string Address
	{
		get
		{
			return _address;
		}
		set
		{
			SetProperty(ref _address, value);
		}
	}

	public string Gstin
	{
		get
		{
			return _gstin;
		}
		set
		{
			SetProperty(ref _gstin, value);
		}
	}

	public string DrugLicenceNumber
	{
		get
		{
			return _drugLicenceNumber;
		}
		set
		{
			SetProperty(ref _drugLicenceNumber, value);
		}
	}

	public bool IsActive
	{
		get
		{
			return _isActive;
		}
		set
		{
			SetProperty(ref _isActive, value);
		}
	}

	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		private set
		{
			SetProperty(ref _errorMessage, value);
		}
	}

	public string StatusMessage
	{
		get
		{
			return _statusMessage;
		}
		private set
		{
			SetProperty(ref _statusMessage, value);
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		List<Supplier> list = await context.Suppliers.AsNoTracking().OrderBy((Supplier item) => item.Name).ToListAsync(cancellationToken);
		Guid? keepId = _editingId;
		Suppliers.Clear();
		foreach (Supplier item in list)
		{
			Suppliers.Add(item);
		}

		if (keepId is Guid id)
		{
			SelectedSupplier = Suppliers.FirstOrDefault((Supplier item) => item.Id == id);
		}
	}

	private void NewSupplier()
	{
		_editingId = null;
		SelectedSupplier = null;
		Name = string.Empty;
		Phone = string.Empty;
		Email = string.Empty;
		Address = string.Empty;
		Gstin = string.Empty;
		DrugLicenceNumber = string.Empty;
		IsActive = true;
		ErrorMessage = string.Empty;
		StatusMessage = string.Empty;
	}

	private void ShowSupplier(Supplier supplier)
	{
		_editingId = supplier.Id;
		Name = supplier.Name;
		Phone = supplier.Phone ?? string.Empty;
		Email = supplier.Email ?? string.Empty;
		Address = supplier.Address ?? string.Empty;
		Gstin = supplier.Gstin ?? string.Empty;
		DrugLicenceNumber = supplier.DrugLicenceNumber ?? string.Empty;
		IsActive = supplier.IsActive;
		ErrorMessage = string.Empty;
		StatusMessage = string.Empty;
	}

	private async Task SaveAsync()
	{
		string name = Name.Trim();
		if (name.Length == 0)
		{
			ErrorMessage = "Enter the supplier name.";
			return;
		}

		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			Guid editing = _editingId ?? Guid.Empty;
			string lowerName = name.ToLower();
			if (await context.Suppliers.AnyAsync((Supplier item) => item.Id != editing && item.Name.ToLower() == lowerName))
			{
				ErrorMessage = "A supplier named " + name + " already exists.";
				return;
			}

			Supplier entity;
			if (_editingId is Guid id)
			{
				entity = await context.Suppliers.SingleAsync((Supplier item) => item.Id == id);
			}
			else
			{
				entity = new Supplier();
				context.Suppliers.Add(entity);
			}

			entity.Name = name;
			entity.Phone = Clean(Phone);
			entity.Email = Clean(Email);
			entity.Address = Clean(Address);
			entity.Gstin = Clean(Gstin);
			entity.DrugLicenceNumber = Clean(DrugLicenceNumber);
			entity.IsActive = IsActive;
			await context.SaveChangesAsync();
			_editingId = entity.Id;
			ErrorMessage = string.Empty;
			StatusMessage = "Supplier saved.";
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = "The supplier could not be saved: " + PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private static string? Clean(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
