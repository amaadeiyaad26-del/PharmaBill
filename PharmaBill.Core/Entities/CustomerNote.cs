namespace PharmaBill.Core.Entities;

/// <summary>Free text kept against a customer's account, with who wrote it and when.</summary>
public sealed class CustomerNote : EntityBase
{
	public Guid CustomerId { get; set; }

	public string Text { get; set; } = string.Empty;

	public Guid? AuthorUserId { get; set; }

	/// <summary>The author's name as it was when the note was written.</summary>
	public string AuthorName { get; set; } = string.Empty;
}
