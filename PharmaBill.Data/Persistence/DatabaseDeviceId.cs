namespace PharmaBill.Data.Persistence;

public sealed class DatabaseDeviceId
{
    public DatabaseDeviceId(Guid value) => Value = value;

    public Guid Value { get; }
}
