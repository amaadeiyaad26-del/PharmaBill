using System;
using System.Collections.Generic;

namespace PharmaBill.Sync;

public sealed record CloudPushAck(int Accepted, int Duplicates, string ServerHlc, IReadOnlyList<Guid> AcknowledgedChangeIds);
