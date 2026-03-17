namespace ForRest.Tests.Domain;

[TestClass]
public sealed class VariableResolverTests
{
    #region Private Fields

    private readonly VariableResolver variableResolver = new();

    #endregion

    #region Public Methods

    [TestMethod]
    public void Resolve_prefers_highest_precedence_scope_and_records_override_chain()
    {
        var resolved = variableResolver.Resolve(
        [
            CreateVariable("token", "system", VariableScope.System),
        ],
        [
            CreateVariable("token", "global", VariableScope.Global),
        ],
        [
            CreateVariable("token", "workspace", VariableScope.Workspace),
        ],
        [
            CreateVariable("token", "environment", VariableScope.Environment, isSecret: true),
        ],
        [
            CreateVariable("token", "request", VariableScope.RequestLocal),
        ],
        [
            CreateVariable("token", "runtime", VariableScope.Runtime),
        ]);

        var variable = resolved.Single(static item => item.Key == "token");

        Assert.AreEqual("runtime", variable.Value);
        Assert.AreEqual(VariableScope.Runtime, variable.EffectiveScope);
        CollectionAssert.AreEqual(
            new[]
            {
                VariableScope.System,
                VariableScope.Global,
                VariableScope.Workspace,
                VariableScope.Environment,
                VariableScope.RequestLocal,
                VariableScope.Runtime,
            },
            variable.OverrideChain.Select(static item => item.Scope).ToArray());
        Assert.IsTrue(variable.IsSecret);
    }

    [TestMethod]
    public void Preview_renders_known_tokens_and_leaves_unknown_tokens_in_place()
    {
        var preview = variableResolver.Preview(
            "https://{{host}}/users/{{id}}?missing={{missing}}",
            [],
            [],
            [
                CreateVariable("host", "api.example.test", VariableScope.Workspace),
            ],
            [],
            [
                CreateVariable("id", "42", VariableScope.RequestLocal),
            ],
            []);

        Assert.AreEqual("https://api.example.test/users/42?missing={{missing}}", preview.RenderedText);
        Assert.HasCount(2, preview.Variables);
    }

    #endregion

    #region Private Methods

    private static VariableDefinition CreateVariable(string key, string value, VariableScope scope, bool isSecret = false)
    {
        return new()
        {
            Key = key,
            Value = value,
            Scope = scope,
            IsSecret = isSecret,
        };
    }

    #endregion
}
