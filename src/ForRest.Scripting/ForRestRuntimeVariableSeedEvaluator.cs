namespace ForRest.Scripting;

public sealed class ForRestRuntimeVariableSeedEvaluator
{
    #region Public Methods

    public List<VariableDefinition> Evaluate(IEnumerable<ForRestRuntimeVariableSeed> seeds)
    {
        return
        [
            .. seeds.Select(
                seed => new VariableDefinition
                {
                    Key = seed.Key,
                    Value = EvaluateSeed(seed),
                    Scope = VariableScope.Runtime,
                    IsSecret = seed.IsSecret,
                }),
        ];
    }

    #endregion

    #region Private Methods

    private static string EvaluateSeed(ForRestRuntimeVariableSeed seed)
    {
        return seed.Kind switch
        {
            ForRestRuntimeSeedKind.Literal => seed.LiteralValue,
            ForRestRuntimeSeedKind.Guid => Guid.NewGuid().ToString(),
            ForRestRuntimeSeedKind.Now => DateTimeOffset.Now.ToString("O"),
            ForRestRuntimeSeedKind.UtcNow => DateTimeOffset.UtcNow.ToString("O"),
            ForRestRuntimeSeedKind.RandomNumber => Random.Shared.Next(seed.MinimumInclusive ?? 0, seed.MaximumExclusive ?? int.MaxValue).ToString(),
            _ => seed.LiteralValue,
        };
    }

    #endregion
}
