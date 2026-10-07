namespace PharmaBill.Sync;

public sealed record GoogleDriveProgress(int Percent, string Message, long BytesSent = 0L, long TotalBytes = 0L, bool IsError = false, bool IsComplete = false);
