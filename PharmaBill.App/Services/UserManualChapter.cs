using System.Collections.Generic;

namespace PharmaBill.App.Services;

public sealed record UserManualChapter(string Id, string Title, string Keywords, IReadOnlyList<GuideBlock> Blocks);
