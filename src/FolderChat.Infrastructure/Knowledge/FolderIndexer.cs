using System.Threading.Channels;
using FolderChat.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FolderChat.Infrastructure.Knowledge;

/// <summary>
/// Runs folder scans in the background, one at a time: for folders that ask for one, and for every folder every few
/// minutes, which picks up files added, changed or deleted since. Keeps each folder's progress for the Settings page.
/// </summary>
internal sealed class FolderIndexer(IServiceScopeFactory scopes, ILogger<FolderIndexer> logger) : BackgroundService, IFolderScanQueue
{
    /// <summary>How often every folder is checked for changes. Unchanged files cost only a look at their size and date.</summary>
    internal static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(5);

    private readonly Channel<Guid> queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, ScanProgress> progress = []; // folders waiting or being scanned
    private readonly HashSet<Guid> again = []; // folders asked for while being scanned
    private Guid? current;
    private CancellationTokenSource? currentCancel;

    public void Enqueue(Guid folderId)
    {
        lock (gate)
        {
            if (current == folderId)
            {
                // Files may have changed after the running scan listed them, so scan again when it ends.
                again.Add(folderId);
            }
            else if (progress.TryAdd(folderId, new ScanProgress(ScanPhase.Waiting, 0, 0)))
            {
                queue.Writer.TryWrite(folderId);
            }
        }
    }

    public ScanProgress? GetProgress(Guid folderId)
    {
        lock (gate)
        {
            return progress.GetValueOrDefault(folderId);
        }
    }

    public void Cancel(Guid folderId)
    {
        lock (gate)
        {
            again.Remove(folderId);
            progress.Remove(folderId); // a waiting scan is skipped when its turn comes
            if (current == folderId)
            {
                currentCancel?.Cancel();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = ScheduleAsync(stoppingToken);
        await foreach (var folderId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            CancellationTokenSource cancel;
            lock (gate)
            {
                if (!progress.ContainsKey(folderId))
                {
                    continue; // cancelled while waiting
                }

                cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                current = folderId;
                currentCancel = cancel;
                progress[folderId] = new ScanProgress(ScanPhase.Reading, 0, 0);
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var scanner = scope.ServiceProvider.GetRequiredService<FolderScanner>();
                await scanner.ScanAsync(folderId, update => Report(folderId, update), cancel.Token);
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                // The folder was removed, or the app is closing.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scanning folder {FolderId} failed", folderId);
            }
            finally
            {
                lock (gate)
                {
                    current = null;
                    currentCancel = null;
                    progress.Remove(folderId);
                    if (again.Remove(folderId) && !stoppingToken.IsCancellationRequested)
                    {
                        progress[folderId] = new ScanProgress(ScanPhase.Waiting, 0, 0);
                        queue.Writer.TryWrite(folderId);
                    }
                }

                cancel.Dispose();
            }
        }

        await schedule;
    }

    private void Report(Guid folderId, ScanProgress update)
    {
        lock (gate)
        {
            if (progress.ContainsKey(folderId))
            {
                progress[folderId] = update;
            }
        }
    }

    /// <summary>Queues every folder now, at startup, and again every <see cref="RescanInterval"/>.</summary>
    private async Task ScheduleAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RescanInterval);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var store = scope.ServiceProvider.GetRequiredService<IKnowledgeStore>();
                    foreach (var folder in await store.ListAsync(ct))
                    {
                        Enqueue(folder.Id);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Couldn't list the knowledge folders to scan");
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
    }
}
