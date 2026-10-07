using System;
using System.Collections.Generic;
using System.Linq;
using PharmaBill.Core.Entities;

namespace PharmaBill.Core.Security;

public static class PermissionMatrix
{
	private static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<AppPermission>> Permissions = new Dictionary<UserRole, IReadOnlySet<AppPermission>>
	{
		[UserRole.Owner] = Enum.GetValues<AppPermission>().ToHashSet(),
		[UserRole.Manager] = Enum.GetValues<AppPermission>().ToHashSet(),
		[UserRole.Pharmacist] = new HashSet<AppPermission>
		{
			AppPermission.View,
			AppPermission.Search,
			AppPermission.Print,
			AppPermission.Export,
			AppPermission.CreateBill,
			AppPermission.EnterStock,
			AppPermission.ProcessReturn,
			AppPermission.ManageCustomers
		},
		[UserRole.BillingClerk] = new HashSet<AppPermission>
		{
			AppPermission.View,
			AppPermission.Search,
			AppPermission.Print,
			AppPermission.Export,
			AppPermission.CreateBill,
			AppPermission.ManageCustomers
		},
		[UserRole.Salesman] = new HashSet<AppPermission>
		{
			AppPermission.View,
			AppPermission.Search,
			AppPermission.CreateBill,
			AppPermission.ManageCustomers
		},
		[UserRole.Accountant] = new HashSet<AppPermission>
		{
			AppPermission.View,
			AppPermission.Search,
			AppPermission.Print,
			AppPermission.Export,
			AppPermission.Backup,
			AppPermission.ViewPurchaseMargins
		}
	};

	public static IReadOnlyList<string> OperatorRoleLabels { get; } = new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "Admin", "Pharmacist", "Cashier", "Admin (Manager)", "Salesman", "Accountant" });

	public static bool Allows(UserRole role, AppPermission permission)
	{
		if (Permissions.TryGetValue(role, out IReadOnlySet<AppPermission> value))
		{
			return value.Contains(permission);
		}
		return false;
	}

	public static bool AllowsInReadOnlyMode(AppPermission permission)
	{
		if ((uint)permission <= 4u)
		{
			return true;
		}
		return false;
	}

	public static string ToOperatorLabel(UserRole role)
	{
		return role switch
		{
			UserRole.Owner => "Admin", 
			UserRole.Manager => "Admin (Manager)", 
			UserRole.Pharmacist => "Pharmacist", 
			UserRole.BillingClerk => "Cashier", 
			UserRole.Salesman => "Salesman", 
			UserRole.Accountant => "Accountant", 
			_ => role.ToString(), 
		};
	}

	public static UserRole FromOperatorLabel(string label)
	{
		UserRole result;
		return label.Trim() switch
		{
			"Admin" => UserRole.Owner, 
			"Admin (Manager)" => UserRole.Manager, 
			"Pharmacist" => UserRole.Pharmacist, 
			"Cashier" => UserRole.BillingClerk, 
			"Salesman" => UserRole.Salesman, 
			"Accountant" => UserRole.Accountant, 
			_ => Enum.TryParse<UserRole>(label, ignoreCase: true, out result) ? result : UserRole.BillingClerk, 
		};
	}
}
