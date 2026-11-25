using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Core.Tests;

public sealed class FailuresTests
{
    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "The test checks that this exception is never treated as recoverable.")]
    public void IsRecoverable_OutOfMemory_IsFalse() =>
        Assert.False(Failures.IsRecoverable(new OutOfMemoryException()));

    [Theory]
    [MemberData(nameof(Recoverable))]
    public void IsRecoverable_AnyOtherException_IsTrue(Exception exception) =>
        Assert.True(Failures.IsRecoverable(exception));

    [Fact]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "The test checks that this exception is never treated as a failure to report.")]
    public void IsFailure_OutOfMemory_IsFalse() =>
        Assert.False(Failures.IsFailure(new OutOfMemoryException()));

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    public void IsFailure_Cancellation_IsFalse(Type type) =>
        Assert.False(Failures.IsFailure((Exception)Activator.CreateInstance(type)!));

    [Theory]
    [MemberData(nameof(Recoverable))]
    public void IsFailure_AnyOtherException_IsTrue(Exception exception) =>
        Assert.True(Failures.IsFailure(exception));

    [Fact]
    public void TypeName_Exception_IsTheFullTypeNameWithoutTheMessage()
    {
        var name = Failures.TypeName(new InvalidOperationException("C:\\Users\\alice\\secret.txt"));

        Assert.Equal("System.InvalidOperationException", name);
    }

    [Fact]
    public void TypeName_NestedType_KeepsTheNesting() =>
        Assert.Equal(typeof(Nested).FullName, Failures.TypeName(new Nested()));

    [Fact]
    public void Predicates_Null_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Failures.IsRecoverable(null!));
        Assert.Throws<ArgumentNullException>(() => Failures.IsFailure(null!));
        Assert.Throws<ArgumentNullException>(() => Failures.TypeName(null!));
    }

    public static TheoryData<Exception> Recoverable() =>
        new()
        {
            new InvalidOperationException(),
            new IOException(),
            new UnauthorizedAccessException(),
            new ObjectDisposedException("x"),
            new AggregateException(),
        };

    private sealed class Nested : Exception;
}
