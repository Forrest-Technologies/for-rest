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

    [TestMethod]
    public void RenderTemplate_returns_empty_for_null_template()
    {
        Assert.AreEqual(string.Empty, VariableResolver.RenderTemplate(null!, []));
    }

    [TestMethod]
    public void RenderTemplate_returns_empty_for_empty_template()
    {
        Assert.AreEqual(string.Empty, VariableResolver.RenderTemplate(string.Empty, []));
    }

    [TestMethod]
    public void RenderTemplate_returns_input_unchanged_when_no_tokens_present()
    {
        const string template = "https://api.example.test/users";
        Assert.AreEqual(template, VariableResolver.RenderTemplate(template, [CreateResolved("host", "ignored")]));
    }

    [TestMethod]
    public void RenderTemplate_leaves_unclosed_token_fragment_intact()
    {
        const string template = "https://{{host/users";
        Assert.AreEqual(template, VariableResolver.RenderTemplate(template, [CreateResolved("host", "api.example.test")]));
    }

    [TestMethod]
    public void RenderTemplate_substitutes_known_tokens_and_leaves_unknown_literal()
    {
        var rendered = VariableResolver.RenderTemplate(
            "https://{{host}}/users/{{id}}?missing={{missing}}",
            [
                CreateResolved("host", "api.example.test"),
                CreateResolved("id", "42"),
            ]);

        Assert.AreEqual("https://api.example.test/users/42?missing={{missing}}", rendered);
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

    private static ResolvedVariable CreateResolved(string key, string value)
    {
        return new()
        {
            Key = key,
            Value = value,
        };
    }

    #endregion
}
