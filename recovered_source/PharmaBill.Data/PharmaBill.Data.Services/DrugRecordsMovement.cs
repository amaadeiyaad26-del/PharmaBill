using System;

namespace PharmaBill.Data.Services;

public sealed record DrugRecordsMovement(Guid DrugId, Guid BatchId, DateTime AtUtc, string RecordType, string DocumentNo, string PatientName, string Address, string Phone, string DoctorName, string DoctorRegistrationNo, string BatchNo, decimal QuantityChange, decimal? BalanceChange = null, decimal? SalesQuantityChange = null, Guid? PartyId = null);
