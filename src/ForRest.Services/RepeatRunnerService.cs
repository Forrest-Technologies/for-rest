namespace ForRest.Services;

public sealed class RepeatRunnerService : IRepeatRunnerService
{
    #region Public Methods

    public async Task<List<T>> Run<T>(
        ScheduleDefinition schedule,
        Func<int, CancellationToken, Task<T>> iteration,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        var repeatCount = Math.Max(1, schedule.RepeatCount);

        if (schedule.DelayMilliseconds > 0)
        {
            await Task.Delay(schedule.DelayMilliseconds, cancellationToken);
        }

        for (var index = 0; index < repeatCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await iteration(index + 1, cancellationToken));

            var isLastIteration = index == repeatCount - 1;
            if (!isLastIteration && schedule.IntervalMilliseconds > 0)
            {
                await Task.Delay(schedule.IntervalMilliseconds, cancellationToken);
            }
        }

        return results;
    }

    #endregion
}
