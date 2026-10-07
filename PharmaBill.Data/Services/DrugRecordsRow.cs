using System;

namespace PharmaBill.Data.Services;

public sealed record DrugRecordsRow(DateTime AtUtc, string RecordType, string DocumentNo, string PatientName, string Address, string Phone, string DoctorName, string DoctorRegistrationNo, string BatchNo, decimal Quantity, decimal RunningBalance);
