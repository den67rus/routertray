#if !MICROSOFT_STORE
using Velopack;
using Velopack.Sources;
#endif

namespace RouterTray;

internal enum ApplicationUpdateCheckResult
{
    UpToDate,
    UpdateScheduled,
    ManagedByPackage,
    NotPackaged,
    Failed
}

internal sealed class AppUpdateService : IDisposable
{
    private const string RepositoryUrl = "https://github.com/den67rus/routertray";
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(14);

    private readonly FileLogger _logger;
    private readonly Action<Action> _scheduleApply;
    private readonly Func<CancellationToken, Task> _runAutomaticLoop;
    private readonly bool _packageManaged;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _runCancellation;
    private DateTimeOffset? _lastCheckAttemptUtc;
    private ApplicationUpdateChannel _channel;
#if !MICROSOFT_STORE
    private bool _updateScheduled;
#endif
    private bool _enabled;
    private bool _started;
    private bool _disposed;

    public AppUpdateService(
        FileLogger logger,
        Action<Action> scheduleApply,
        bool enabled,
        ApplicationUpdateChannel channel,
        Func<CancellationToken, Task>? runAutomaticLoop = null,
        bool packageManaged = false)
    {
        ValidateChannel(channel);
        _logger = logger;
        _scheduleApply = scheduleApply;
        _runAutomaticLoop = runAutomaticLoop ?? RunAsync;
        _enabled = enabled;
        _channel = channel;
#if MICROSOFT_STORE
        _packageManaged = true;
#else
        _packageManaged = packageManaged;
#endif
    }

    public void Start()
    {
        CancellationTokenSource? runCancellation = null;
        ApplicationUpdateChannel channel;
        lock (_sync)
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;
            channel = _channel;
            if (_enabled && !_packageManaged)
            {
                runCancellation = CreateRunCancellationLocked();
            }
        }

        if (_packageManaged)
        {
            _logger.Info("Application updates are managed by the installed app package.");
            return;
        }

        if (runCancellation is null)
        {
            _logger.Info("Automatic update checks are disabled in application settings.");
            return;
        }

        _logger.Info($"Automatic update checks enabled for the {channel} channel.");
        StartRun(runCancellation);
    }

    public void SetConfiguration(bool enabled, ApplicationUpdateChannel channel)
    {
        ValidateChannel(channel);
        CancellationTokenSource? runCancellation = null;
        CancellationTokenSource? cancellationToStop = null;
        bool enabledChanged;
        bool channelChanged;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            enabledChanged = _enabled != enabled;
            channelChanged = _channel != channel;
            if (!enabledChanged && !channelChanged)
            {
                return;
            }

            _enabled = enabled;
            _channel = channel;
            if (channelChanged)
            {
                _lastCheckAttemptUtc = null;
            }

            if (_started && !_packageManaged)
            {
                if (enabled && (enabledChanged || channelChanged))
                {
                    cancellationToStop = _runCancellation;
                    _runCancellation = null;
                    runCancellation = CreateRunCancellationLocked();
                }
                else if (!enabled && enabledChanged)
                {
                    cancellationToStop = _runCancellation;
                    _runCancellation = null;
                }
            }
        }

        if (channelChanged)
        {
            _logger.Info($"Application update channel changed to {channel}.");
        }

        CancelRun(cancellationToStop);
        if (runCancellation is not null)
        {
            StartRun(runCancellation);
        }

        if (!enabledChanged)
        {
            return;
        }

        _logger.Info(enabled
            ? "Automatic update checks enabled."
            : "Automatic update checks disabled.");
    }

    public async Task<ApplicationUpdateCheckResult> CheckNowAsync(
        ApplicationUpdateChannel channel,
        CancellationToken cancellationToken)
    {
        ValidateChannel(channel);
        if (_packageManaged)
        {
            _logger.Info("Manual update checks are delegated to the installed app package.");
            return ApplicationUpdateCheckResult.ManagedByPackage;
        }

        CancellationToken lifetimeToken;
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AppUpdateService));
            }

            lifetimeToken = _lifetimeCancellation.Token;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetimeToken);
        try
        {
            return await CheckForUpdatesSerializedAsync(channel, linkedCancellation.Token);
        }
        finally
        {
            RescheduleAutomaticChecksAfterManualCheck();
        }
    }

    private CancellationTokenSource CreateRunCancellationLocked()
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _runCancellation = cancellation;
        return cancellation;
    }

    private void StartRun(CancellationTokenSource runCancellation)
    {
        _ = RunAndCleanUpAsync(runCancellation);
    }

    private async Task RunAndCleanUpAsync(CancellationTokenSource runCancellation)
    {
        try
        {
            await _runAutomaticLoop(runCancellation.Token);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_runCancellation, runCancellation))
                {
                    _runCancellation = null;
                }
            }

            runCancellation.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(GetDelayBeforeAutomaticCheck(), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await CheckForUpdatesSerializedAsync(
                    GetConfiguredChannel(),
                    cancellationToken);
                if (result is ApplicationUpdateCheckResult.NotPackaged or
                    ApplicationUpdateCheckResult.UpdateScheduled)
                {
                    return;
                }

                await Task.Delay(CheckInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Automatic update loop failed.", ex);
        }
    }

    private async Task<ApplicationUpdateCheckResult> CheckForUpdatesSerializedAsync(
        ApplicationUpdateChannel channel,
        CancellationToken cancellationToken)
    {
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            return await CheckAndDownloadUpdateAsync(channel, cancellationToken);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private async Task<ApplicationUpdateCheckResult> CheckAndDownloadUpdateAsync(
        ApplicationUpdateChannel channel,
        CancellationToken cancellationToken)
    {
#if MICROSOFT_STORE
        await Task.CompletedTask;
        return ApplicationUpdateCheckResult.ManagedByPackage;
#else
        try
        {
            lock (_sync)
            {
                if (_updateScheduled)
                {
                    return ApplicationUpdateCheckResult.UpdateScheduled;
                }
            }

            var includePrereleases = IncludesPrereleases(channel);
            var source = new GithubSource(
                RepositoryUrl,
                accessToken: null,
                prerelease: includePrereleases);
            var manager = new UpdateManager(source);

            if (!manager.IsInstalled)
            {
                _logger.Info("Updates are unavailable for an unpackaged development build.");
                return ApplicationUpdateCheckResult.NotPackaged;
            }

            RecordCheckAttempt(channel, cancellationToken, DateTimeOffset.UtcNow);
            _logger.Info(
                $"Checking GitHub Releases for application updates " +
                $"(channel: {channel}, prereleases: {includePrereleases}).");
            var update = await manager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (update is null)
            {
                _logger.Info("No application update is available.");
                return ApplicationUpdateCheckResult.UpToDate;
            }

            _logger.Info(
                $"Downloading RouterTray {update.TargetFullRelease.Version} " +
                $"({update.DeltasToTarget.Length} delta package(s)).");
            var lastLoggedProgress = -10;
            await manager.DownloadUpdatesAsync(
                update,
                progress =>
                {
                    if (progress < 100 && progress < lastLoggedProgress + 10)
                    {
                        return;
                    }

                    lastLoggedProgress = progress;
                    _logger.Info($"Update download progress: {progress}%.");
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            _logger.Info(
                $"RouterTray {update.TargetFullRelease.Version} is ready; scheduling restart.");
            _scheduleApply(() => manager.WaitExitThenApplyUpdates(
                update.TargetFullRelease,
                silent: true,
                restart: true));
            lock (_sync)
            {
                _updateScheduled = true;
            }

            return ApplicationUpdateCheckResult.UpdateScheduled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Application update check failed.", ex);
            return ApplicationUpdateCheckResult.Failed;
        }
#endif
    }

    private void RecordCheckAttempt(
        ApplicationUpdateChannel channel,
        CancellationToken cancellationToken,
        DateTimeOffset attemptedAtUtc)
    {
        lock (_sync)
        {
            if (!cancellationToken.IsCancellationRequested && _channel == channel)
            {
                _lastCheckAttemptUtc = attemptedAtUtc;
            }
        }
    }

    private TimeSpan GetDelayBeforeAutomaticCheck()
    {
        DateTimeOffset? lastCheckAttemptUtc;
        lock (_sync)
        {
            lastCheckAttemptUtc = _lastCheckAttemptUtc;
        }

        if (lastCheckAttemptUtc is null)
        {
            return InitialDelay;
        }

        var utcNow = DateTimeOffset.UtcNow;
        var effectiveLastAttempt = lastCheckAttemptUtc.Value > utcNow
            ? utcNow
            : lastCheckAttemptUtc.Value;
        var remaining = effectiveLastAttempt + CheckInterval - utcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private ApplicationUpdateChannel GetConfiguredChannel()
    {
        lock (_sync)
        {
            return _channel;
        }
    }

    private void RescheduleAutomaticChecksAfterManualCheck()
    {
        CancellationTokenSource? cancellationToStop = null;
        CancellationTokenSource? runCancellation = null;
        lock (_sync)
        {
            if (_disposed || !_started || !_enabled || _packageManaged)
            {
                return;
            }

            cancellationToStop = _runCancellation;
            _runCancellation = null;
            runCancellation = CreateRunCancellationLocked();
        }

        CancelRun(cancellationToStop);
        StartRun(runCancellation!);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellationToStop;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _enabled = false;
            cancellationToStop = _runCancellation;
            _runCancellation = null;
        }

        _lifetimeCancellation.Cancel();
        CancelRun(cancellationToStop);
        GC.SuppressFinalize(this);
    }

    private static void CancelRun(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run completed and disposed its cancellation source after it was
            // detached under the service lock.
        }
    }

    internal static bool IncludesPrereleases(ApplicationUpdateChannel channel)
    {
        ValidateChannel(channel);
        return channel == ApplicationUpdateChannel.Preview;
    }

    private static void ValidateChannel(ApplicationUpdateChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported update channel.");
        }
    }
}
