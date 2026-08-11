namespace RouterTray.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Acquire_AllowsOnlyOneOwnerForNamedMutex()
    {
        var name = $@"Local\RouterTray.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.Acquire(name);

        Assert.True(first.IsPrimaryInstance);

        using (var second = SingleInstanceGuard.Acquire(name))
        {
            Assert.False(second.IsPrimaryInstance);
        }

        first.Dispose();
        using var replacement = SingleInstanceGuard.Acquire(name);
        Assert.True(replacement.IsPrimaryInstance);
    }
}
