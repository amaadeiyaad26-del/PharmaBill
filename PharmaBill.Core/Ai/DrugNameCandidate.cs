using System;

namespace PharmaBill.Core.Ai;

public sealed record DrugNameCandidate(Guid Id, string Name, string? BrandName, string? GenericName);
