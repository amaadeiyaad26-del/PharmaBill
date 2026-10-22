using PharmaBill.Core;
using Xunit;

namespace PharmaBill.Tests;

// Developer text never reaches a shopkeeper's screen.
public sealed class UserMessageTests
{
    [Fact]
    public void ParameterNamesAreRemoved()
    {
        Assert.Equal("Use a backup password of at least 10 characters.", UserMessage.Of(new ArgumentException("Use a backup password of at least 10 characters.", "password")));
    }

    [Fact]
    public void FrameworkWording_NamesTheMissingField()
    {
        Exception empty = Assert.ThrowsAny<ArgumentException>(() => ArgumentException.ThrowIfNullOrWhiteSpace("  ", "input.PrescriberRegistrationNumber"));
        Assert.Equal("Prescriber registration number is required.", UserMessage.Of(empty));
        Exception missing = Assert.Throws<ArgumentNullException>(() => ArgumentNullException.ThrowIfNull((object?)null, "input.PatientName"));
        Assert.Equal("Patient name is required.", UserMessage.Of(missing));
    }

    [Fact]
    public void ProgramFaultsBecomeAPlainApology_AndOurMessagesPassThrough()
    {
        Assert.Equal(UserMessage.Fallback, UserMessage.Of(new NullReferenceException()));
        Assert.Equal("Quantity exceeds available stock.", UserMessage.Of(new AggregateException(new InvalidOperationException("Quantity exceeds available stock."))));
    }
}
