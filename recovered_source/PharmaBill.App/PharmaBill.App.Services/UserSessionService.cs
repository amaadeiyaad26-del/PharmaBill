using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;

namespace PharmaBill.App.Services;

public sealed class UserSessionService(CurrentSession currentSession) : IUserSessionService
{
	public AppUser? CurrentUser => currentSession.User;

	public bool IsAuthenticated => currentSession.IsAuthenticated;

	public UserRole? CurrentRole => currentSession.User?.Role;

	public string OperatorRoleLabel
	{
		get
		{
			if (currentSession.User != null)
			{
				return PermissionMatrix.ToOperatorLabel(currentSession.User.Role);
			}
			return "Signed out";
		}
	}

	public bool CanViewPurchaseMargins => Allows(AppPermission.ViewPurchaseMargins);

	public bool CanEditPrintedBills => Allows(AppPermission.EditPrintedBills);

	public bool CanAccessSettings => Allows(AppPermission.ManageSettings);

	public bool CanManageUsers => Allows(AppPermission.ManageUsers);

	public bool CanCreateBill => Allows(AppPermission.CreateBill);

	public bool Allows(AppPermission permission)
	{
		if (currentSession.User != null)
		{
			return PermissionMatrix.Allows(currentSession.User.Role, permission);
		}
		return false;
	}
}
