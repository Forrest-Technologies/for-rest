using System.Collections.Generic;
using System.Linq;
using ForRest.Scripting;

namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class VariablesApiSecretTests
{
    #region Public Methods

    [TestMethod]
    public void Set_preserves_secret_flag_when_reassigning_an_existing_secret_variable()
    {
        VariablesApi variables = new(
        [
            new VariableDefinition
            {
                Key = "api_key",
                Value = "original",
                Scope = VariableScope.Workspace,
                IsSecret = true,
            },
        ]);

        variables.Set("api_key", "rotated-at-runtime");

        VariableDefinition updated = variables.All().Single(item => item.Key == "api_key");
        Assert.AreEqual("rotated-at-runtime", updated.Value);
        Assert.IsTrue(updated.IsSecret, "reassigning a secret variable must keep it secret so it stays DPAPI-protected at rest");
    }

    [TestMethod]
    public void Set_can_promote_a_variable_to_secret_explicitly()
    {
        VariablesApi variables = new([]);

        variables.Set("token", "abc", VariableScope.Runtime, isSecret: true);

        Assert.IsTrue(variables.All().Single(item => item.Key == "token").IsSecret);
    }

    [TestMethod]
    public void MergeRuntimeVariables_does_not_downgrade_an_existing_secret()
    {
        VariablesApi variables = new(
        [
            new VariableDefinition
            {
                Key = "session",
                Value = "old",
                Scope = VariableScope.Runtime,
                IsSecret = true,
            },
        ]);

        variables.MergeRuntimeVariables(
        [
            new VariableDefinition
            {
                Key = "session",
                Value = "new",
                Scope = VariableScope.Runtime,
                IsSecret = false,
            },
        ]);

        VariableDefinition merged = variables.RuntimeVariables().Single(item => item.Key == "session");
        Assert.AreEqual("new", merged.Value);
        Assert.IsTrue(merged.IsSecret, "an incoming plaintext-flagged value must not strip an existing secret flag");
    }

    #endregion
}
