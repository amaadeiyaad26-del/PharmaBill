using System;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;

namespace PharmaBill.App.Services;

public static class WelcomeGreeting
{
	public static string GetTimeGreeting(DateTime localNow)
	{
		int hour = localNow.Hour;
		if (hour >= 5 && hour <= 11)
		{
			return "Good Morning";
		}
		if (hour >= 12 && hour <= 16)
		{
			return "Good Afternoon";
		}
		return "Good Evening";
	}

	public static string ResolveDisplayName(AppUser? user)
	{
		if (string.IsNullOrWhiteSpace(user?.DisplayName))
		{
			if (string.IsNullOrWhiteSpace(user?.UserName))
			{
				return "Pharmacist";
			}
			return user.UserName.Trim();
		}
		return user.DisplayName.Trim();
	}

	public static string ResolveRoleLabel(AppUser? user)
	{
		if (user != null)
		{
			return PermissionMatrix.ToOperatorLabel(user.Role);
		}
		return "Operator";
	}

	public static string BuildHeadline(DateTime localNow, AppUser? user)
	{
		return GetTimeGreeting(localNow) + ", " + ResolveDisplayName(user) + "!";
	}

	public static string BuildRoleStoreLine(AppUser? user, string? pharmacyName)
	{
		string text = (string.IsNullOrWhiteSpace(pharmacyName) ? "Your Pharmacy" : pharmacyName.Trim());
		return ResolveRoleLabel(user) + " • " + text;
	}

	public static string BuildStatusLine(EntitlementStatus status)
	{
		if (status != EntitlementStatus.EXPIRED)
		{
			return "PharmaBill is ready for dispensing • Workstation active";
		}
		return "Workstation is read-only — renew drug licences to resume billing";
	}
}
