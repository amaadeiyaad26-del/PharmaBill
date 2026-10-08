using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class PharmacySetupService(IUnitOfWork unitOfWork, ProtectedAccessStateStore protectedAccessStateStore)
{
	[SupportedOSPlatform("windows")]
	public async Task<AppUser> CompleteSetupAsync(PharmacyProfile profile, IEnumerable<LicenceRecord> licences, AppUser owner, string ownerSecret, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		ArgumentNullException.ThrowIfNull(owner, "owner");
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerSecret, "ownerSecret");
		List<LicenceRecord> licenceList = licences.ToList();
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Trial state is protected with Windows DPAPI and Registry.");
		}
		PharmacyProfile existing = await unitOfWork.Context.PharmacyProfiles.OrderBy((PharmacyProfile item) => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
		DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
		IEnumerable<LicenceRecord> licences2 = licenceList;
		if (existing != null)
		{
			licences2 = (await unitOfWork.Context.LicenceRecords.Where((LicenceRecord item) => !item.IsDeleted).ToListAsync(cancellationToken)).Concat(licenceList);
		}
		IReadOnlyList<string> missingLicenceTypes = ModeGuard.GetMissingLicenceTypes(profile.BusinessMode, licences2, today);
		if (missingLicenceTypes.Count > 0)
		{
			throw new InvalidOperationException("Setup requires: " + string.Join(", ", missingLicenceTypes) + ".");
		}
		return (existing != null) ? (await ReconfigureAsync(existing, profile, licenceList, owner, ownerSecret, cancellationToken)) : (await CompleteFirstRunAsync(profile, licenceList, owner, ownerSecret, cancellationToken));
	}

	[SupportedOSPlatform("windows")]
	private async Task<AppUser> CompleteFirstRunAsync(PharmacyProfile profile, List<LicenceRecord> licenceList, AppUser owner, string ownerSecret, CancellationToken cancellationToken)
	{
		DateTime now = DateTime.UtcNow;
		profile.TrialStartedAtUtc = now;
		profile.LastEntitlementCheckAtUtc = now;
		owner.Role = UserRole.Owner;
		owner.PasswordHash = PasswordHasher.Hash(ownerSecret);
		owner.IsActive = true;
		owner.IdleLockMinutes = Math.Clamp(owner.IdleLockMinutes, 1, 240);
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			unitOfWork.Context.PharmacyProfiles.Add(profile);
			unitOfWork.Context.LicenceRecords.AddRange(licenceList);
			unitOfWork.Context.AppUsers.Add(owner);
			unitOfWork.Context.AuditLogs.Add(new AuditLog
			{
				UserId = owner.Id,
				ActionAtUtc = now,
				Action = "FirstRunSetupCompleted",
				EntityName = "PharmacyProfile",
				EntityId = profile.Id,
				// Branch may not exist yet during first-run setup; leave null.
				BranchId = null,
				Details = $"Business mode: {profile.BusinessMode}"
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			WriteAccessState(now);
			await transaction.CommitAsync(cancellationToken);
		}
		return owner;
	}

	[SupportedOSPlatform("windows")]
	private async Task<AppUser> ReconfigureAsync(PharmacyProfile existing, PharmacyProfile profile, List<LicenceRecord> licenceList, AppUser ownerDraft, string ownerSecret, CancellationToken cancellationToken)
	{
		DateTime now = DateTime.UtcNow;
		ApplyProfileFields(existing, profile, now);
		string userName = ownerDraft.UserName.Trim();
		AppUser appUser = await unitOfWork.Context.AppUsers.FirstOrDefaultAsync((AppUser user) => !user.IsDeleted && user.UserName == userName, cancellationToken);
		AppUser owner;
		if (appUser == null)
		{
			owner = ownerDraft;
			owner.Role = UserRole.Owner;
			owner.PasswordHash = PasswordHasher.Hash(ownerSecret);
			owner.IsActive = true;
			owner.IdleLockMinutes = Math.Clamp(owner.IdleLockMinutes, 1, 240);
			unitOfWork.Context.AppUsers.Add(owner);
		}
		else
		{
			owner = appUser;
			owner.DisplayName = ownerDraft.DisplayName.Trim();
			owner.Phone = ownerDraft.Phone;
			owner.Email = ownerDraft.Email;
			owner.Role = UserRole.Owner;
			owner.PasswordHash = PasswordHasher.Hash(ownerSecret);
			owner.IsActive = true;
			owner.IdleLockMinutes = Math.Clamp(ownerDraft.IdleLockMinutes, 1, 240);
			owner.AuthProvider = ownerDraft.AuthProvider;
			owner.ProviderSubjectId = ownerDraft.ProviderSubjectId;
			owner.UpdatedAtUtc = now;
			owner.IsDeleted = false;
		}
		unitOfWork.Context.LicenceRecords.AddRange(licenceList);
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = owner.Id,
			ActionAtUtc = now,
			Action = "PharmacyProfileReconfigured",
			EntityName = "PharmacyProfile",
			EntityId = existing.Id,
			BranchId = null,
			Details = $"Business mode: {existing.BusinessMode}; admin: {owner.UserName}"
		});
		AppUser result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			await unitOfWork.SaveChangesAsync(cancellationToken);
			if (!existing.TrialStartedAtUtc.HasValue)
			{
				WriteAccessState(now);
			}
			await transaction.CommitAsync(cancellationToken);
			result = owner;
		}
		return result;
	}

	[SupportedOSPlatform("windows")]
	private void WriteAccessState(DateTime now)
	{
		protectedAccessStateStore.Write(new AccessState(now, now));
	}

	private static void ApplyProfileFields(PharmacyProfile existing, PharmacyProfile profile, DateTime now)
	{
		existing.Name = profile.Name.Trim();
		existing.LegalName = profile.LegalName;
		existing.Address = profile.Address;
		existing.Phone = profile.Phone;
		existing.Email = profile.Email;
		existing.Gstin = profile.Gstin;
		existing.Pan = profile.Pan;
		existing.BusinessMode = profile.BusinessMode;
		existing.CompetentPersonName = profile.CompetentPersonName;
		existing.CompetentPersonQualification = profile.CompetentPersonQualification;
		existing.CompetentPersonRegistrationNumber = profile.CompetentPersonRegistrationNumber;
		existing.BankName = profile.BankName;
		existing.BankAccountName = profile.BankAccountName;
		existing.BankAccountNumber = profile.BankAccountNumber;
		existing.BankIfsc = profile.BankIfsc;
		existing.UpiId = profile.UpiId;
		existing.InvoicePrefix = (string.IsNullOrWhiteSpace(profile.InvoicePrefix) ? existing.InvoicePrefix : profile.InvoicePrefix.Trim());
		existing.WholesaleLicenceTypesJson = profile.WholesaleLicenceTypesJson;
		existing.LastEntitlementCheckAtUtc = now;
		DateTime? trialStartedAtUtc = existing.TrialStartedAtUtc;
		DateTime valueOrDefault = trialStartedAtUtc.GetValueOrDefault();
		if (!trialStartedAtUtc.HasValue)
		{
			valueOrDefault = now;
			DateTime? trialStartedAtUtc2 = valueOrDefault;
			existing.TrialStartedAtUtc = trialStartedAtUtc2;
		}
		existing.UpdatedAtUtc = now;
	}
}
