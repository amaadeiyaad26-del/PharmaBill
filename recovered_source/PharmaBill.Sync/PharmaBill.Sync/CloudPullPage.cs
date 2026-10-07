using System.Collections.Generic;

namespace PharmaBill.Sync;

public sealed record CloudPullPage(IReadOnlyList<CloudChangeWire> Changes, string NextSince, bool HasMore, string ServerHlc);
