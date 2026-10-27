using ForRest.Mcp;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class SecretEditRedactionTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("clé", "mcp", false)]
    [DataRow("ключ", "mcp", false)]
    [DataRow("令牌", "mcp", false)]
    [DataRow("api-token", "mcp", false)]
    [DataRow("token.v2", "mcp", false)]
    [DataRow("api_key", "mcp", true)]
    [DataRow("clé", "sharing", false)]
    [DataRow("ключ", "sharing", false)]
    [DataRow("令牌", "sharing", false)]
    [DataRow("api-token", "sharing", false)]
    [DataRow("token.v2", "sharing", false)]
    [DataRow("api_key", "sharing", true)]
    public void Declaration_values_stay_redacted_while_names_are_being_edited(string name, string surface, bool validIdentifier)
    {
        var source = $"secret {name} = \"fake-private-credential-8261\"\nmethod GET\nurl \"https://fixture.test/\"";
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.AreEqual(validIdentifier, compilation.Succeeded);
        if (validIdentifier)
        {
            Assert.IsTrue(compilation.Payload!.RuntimeSeeds.Single().IsSecret);
        }

        var redacted = surface == "mcp" ? McpSecretRedactor.Redact(source) : FrsSecretRedactor.Redact(source);

        Assert.DoesNotContain("fake-private-credential-8261", redacted);
        StringAssert.Contains(redacted, $"secret {name} = \"***\"");
        Assert.IsTrue(FrsSecretRedactor.ContainsSecrets(source));
    }

    [TestMethod]
    [DataRow("clé")]
    [DataRow("ключ")]
    [DataRow("令牌")]
    [DataRow("api-token")]
    [DataRow("token.v2")]
    [DataRow("api_key")]
    public void Mcp_restores_secret_declarations_after_an_edit(string name)
    {
        var original = $"secret {name} = \"fake-private-credential-8261\"\nmethod GET";
        var replacement = $"secret {name} = \"***\"\nmethod POST";

        var restored = McpSecretRedactor.Restore(original, replacement);

        StringAssert.Contains(restored, $"secret {name} = \"fake-private-credential-8261\"");
        StringAssert.Contains(restored, "method POST");
    }

    #endregion
}
