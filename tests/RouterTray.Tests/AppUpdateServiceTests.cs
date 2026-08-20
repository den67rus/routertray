using System.Threading.Channels;

namespace RouterTray.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public void IncludesPrereleases_UsesConfiguredChannel()
    {
        Assert.False(AppUpdateService.IncludesPrereleases(ApplicationUpdateChannel.Stable));
        Assert.True(AppUpdateService.IncludesPrereleases(ApplicationUpdateChannel.Preview));
    }

    [Fact]
    public void IncludesPrereleases_RejectsUnsupportedChannel()
    {
        var invalidChannel = (ApplicationUpdateChannel)999;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AppUpdateService.IncludesPrereleases(invalidChannel));
    }

    [Fact]
    public async Task SetConfiguration_ChannelChangeRestartsAutomaticLoop()
    {
        var startedRuns = Channel.CreateUnbounded<CancellationToken>();
        var logPath = Path.Combine(
            Path.GetTempPath(),
            $"routertray-update-test-{Guid.NewGuid():N}.log");
        using var logger = new FileLogger(logPath);
        using var service = new AppUpdateService(
            logger,
            _ => { },
            enabled: true,
            channel: ApplicationUpdateChannel.Stable,
            async cancellationToken =>
            {
                startedRuns.Writer.TryWrite(cancellationToken);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            });

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            service.Start();
            var stableRun = await startedRuns.Reader.ReadAsync(timeout.Token);

            service.SetConfiguration(enabled: true, channel: ApplicationUpdateChannel.Preview);

            var previewRun = await startedRuns.Reader.ReadAsync(timeout.Token);
            Assert.True(stableRun.IsCancellationRequested);
            Assert.False(previewRun.IsCancellationRequested);
        }
        finally
        {
            service.Dispose();
            File.Delete(logPath);
        }
    }
}
