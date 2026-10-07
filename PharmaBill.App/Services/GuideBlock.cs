namespace PharmaBill.App.Services;

public sealed record GuideBlock(GuideBlockKind Kind, string Text, string? Detail = null);
