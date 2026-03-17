namespace ForRest.Tests.Services;

[TestClass]
public sealed class RepeatRunnerServiceTests
{
    #region Private Fields

    private readonly RepeatRunnerService repeatRunnerService = new();

    #endregion

    #region Public Methods

    [TestMethod]
    public async Task Run_executes_each_iteration_in_order()
    {
        var results = await repeatRunnerService.Run(
            new()
            {
                RepeatCount = 3,
            },
            static (iteration, _) => Task.FromResult(iteration));

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, results);
    }

    [TestMethod]
    public async Task Run_normalizes_repeat_count_below_one()
    {
        var invocationCount = 0;

        var results = await repeatRunnerService.Run(
            new()
            {
                RepeatCount = 0,
            },
            (_, _) =>
            {
                invocationCount++;
                return Task.FromResult("ran");
            });

        Assert.AreEqual(1, invocationCount);
        CollectionAssert.AreEqual(new[] { "ran" }, results);
    }

    [TestMethod]
    public async Task Run_honors_cancellation_before_iteration()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () =>
            {
                await repeatRunnerService.Run(
                    new()
                    {
                        RepeatCount = 2,
                        DelayMilliseconds = 10,
                    },
                    static (iteration, _) => Task.FromResult(iteration),
                    cancellationTokenSource.Token);
            });
    }

    #endregion
}
