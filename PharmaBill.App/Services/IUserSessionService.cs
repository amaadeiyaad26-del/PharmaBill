using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;

namespace PharmaBill.App.Services;

public interface IUserSessionService
{
	AppUser? CurrentUser { get; }

	bool IsAuthenticated { get; }

	UserRole? CurrentRole { get; }

	string OperatorRoleLabel { get; }

	bool CanViewPurchaseMargins { get; }

	bool CanEditPrintedBills { get; }

	bool CanAccessSettings { get; }

	bool CanManageUsers { get; }

	bool CanCreateBill { get; }

	bool Allows(AppPermission permission);
}
