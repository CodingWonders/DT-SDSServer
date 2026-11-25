using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Jobs;

/// <summary>Handle given to a running job so it can report progress.</summary>
public sealed class JobContext
{
    private readonly Action<int> _report;

    internal JobContext(Guid id, Action<int> report)
    {
        Id = id;
        _report = report;
    }

    /// <summary>Job id.</summary>
    public Guid Id { get; }

    /// <summary>Reports progress (0 to 100). Calls are throttled before reaching the database.</summary>
    public void Report(int percent) => _report(Math.Clamp(percent, 0, 100));

    /// <summary>Progress sink usable with wimgapi wrappers.</summary>
    public IProgress<int> Progress => new Imaging.InlineProgress<int>(Report);
}

/// <summary>
/// Queue of long running operations (image add, export, copy, compaction, driver import).
/// Jobs are recorded in the database so clients can poll GET /api/jobs/{id}; the work itself
/// is an in memory delegate executed by <see cref="JobRunner"/>.
/// </summary>
public sealed class JobService(IDbContextFactory<SdsDbContext> dbFactory, ILogger<JobService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Channel<(Guid Id, Func<JobContext, CancellationToken, Task<object?>> Work)> _channel =
        Channel.CreateUnbounded<(Guid, Func<JobContext, CancellationToken, Task<object?>>)>();

    /// <summary>Reader used by the runner.</summary>
    internal ChannelReader<(Guid Id, Func<JobContext, CancellationToken, Task<object?>> Work)> Reader => _channel.Reader;

    /// <summary>Records a job and queues its work.</summary>
    /// <param name="type">Job type, for example AddImage.</param>
    /// <param name="description">Human readable description.</param>
    /// <param name="requestedBy">User name.</param>
    /// <param name="work">The work; its return value is serialised as the job result.</param>
    /// <param name="ct">Cancellation of the request (not of the job).</param>
    public async Task<Job> EnqueueAsync(string type, string description, string? requestedBy, Func<JobContext, CancellationToken, Task<object?>> work, CancellationToken ct = default)
    {
        var job = new Job { Type = type, Description = description, RequestedBy = requestedBy };
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.Jobs.Add(job);
            await db.SaveChangesAsync(ct);
        }

        await _channel.Writer.WriteAsync((job.Id, work), ct);
        logger.LogInformation("Queued job {JobId} {Type}: {Description}", job.Id, type, description);
        return job;
    }

    /// <summary>Returns a job.</summary>
    public async Task<Job> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct)
            ?? throw new SdsNotFoundException($"Job {id} not found.");
    }

    /// <summary>Lists recent jobs, newest first.</summary>
    public async Task<List<Job>> ListAsync(int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Jobs.AsNoTracking().OrderByDescending(j => j.CreatedUtc).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
    }

    /// <summary>Marks jobs that were queued or running when the process stopped as failed.</summary>
    public async Task FailInterruptedJobsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Jobs.Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.Error, "The server stopped before the job completed.")
                .SetProperty(j => j.CompletedUtc, DateTime.UtcNow), ct);
    }

    internal async Task UpdateAsync(Guid id, Action<Job> change)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FirstAsync(j => j.Id == id);
        change(job);
        await db.SaveChangesAsync();
    }

    internal static string Serialize(object? result) => JsonSerializer.Serialize(result, Json);
}

/// <summary>Hosted service executing queued jobs with bounded parallelism.</summary>
public sealed class JobRunner(JobService jobs, IOptions<SdsOptions> options, ILogger<JobRunner> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One reader loop per allowed concurrent job.
        var workers = Enumerable.Range(0, Math.Max(1, options.Value.MaxConcurrentJobs)).Select(_ => WorkerAsync(stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task WorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var (id, work) in jobs.Reader.ReadAllAsync(stoppingToken))
        {
            await jobs.UpdateAsync(id, j => { j.Status = JobStatus.Running; j.StartedUtc = DateTime.UtcNow; });

            // Throttle progress writes: only persist when the value changes by at least
            // one percent and not more than once per second.
            int lastSaved = -1;
            var lastWrite = DateTime.MinValue;
            var finished = false;
            var gate = new object();
            var context = new JobContext(id, percent =>
            {
                lock (gate)
                {
                    // Ignore late reports once the job finished so a stale value never
                    // overwrites the final 100.
                    if (finished || percent == lastSaved || (DateTime.UtcNow - lastWrite).TotalSeconds < 1)
                    {
                        return;
                    }

                    lastSaved = percent;
                    lastWrite = DateTime.UtcNow;
                }

                _ = jobs.UpdateAsync(id, j => j.Progress = percent);
            });

            try
            {
                // Run on the thread pool: wimgapi calls are long and blocking.
                var result = await Task.Run(() => work(context, stoppingToken), stoppingToken);
                lock (gate)
                {
                    finished = true;
                }

                await jobs.UpdateAsync(id, j =>
                {
                    j.Status = JobStatus.Succeeded;
                    j.Progress = 100;
                    j.ResultJson = JobService.Serialize(result);
                    j.CompletedUtc = DateTime.UtcNow;
                });
                logger.LogInformation("Job {JobId} succeeded", id);
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    finished = true;
                }

                logger.LogError(ex, "Job {JobId} failed", id);
                await jobs.UpdateAsync(id, j =>
                {
                    j.Status = JobStatus.Failed;
                    j.Error = ex.Message;
                    j.CompletedUtc = DateTime.UtcNow;
                });
            }
        }
    }
}
