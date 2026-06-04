using ForRest.Browser;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class BrowserTargetTests
{
    [TestMethod]
    public void Parse_reads_explicit_prefixes()
    {
        Assert.AreEqual(BrowserTargetKind.Css, BrowserTarget.Parse("css=.btn").Kind);
        Assert.AreEqual(BrowserTargetKind.Xpath, BrowserTarget.Parse("xpath=//a").Kind);
        Assert.AreEqual(BrowserTargetKind.Text, BrowserTarget.Parse("text=Save").Kind);
        Assert.AreEqual(BrowserTargetKind.TestId, BrowserTarget.Parse("testid=submit").Kind);
    }

    [TestMethod]
    public void Parse_reads_role_with_optional_name()
    {
        BrowserTarget role = BrowserTarget.Parse("role=button:Save");
        Assert.AreEqual(BrowserTargetKind.Role, role.Kind);
        Assert.AreEqual("button", role.Value);
        Assert.AreEqual("Save", role.Name);

        BrowserTarget bare = BrowserTarget.Parse("role=link");
        Assert.AreEqual("link", bare.Value);
        Assert.IsNull(bare.Name);
    }

    [TestMethod]
    public void Parse_infers_bare_values()
    {
        Assert.AreEqual(BrowserTargetKind.Xpath, BrowserTarget.Parse("//div[@id='x']").Kind);
        Assert.AreEqual(BrowserTargetKind.Css, BrowserTarget.Parse("#main .row").Kind);
    }

    [TestMethod]
    public void Parse_rejects_empty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BrowserTarget.Parse("  "));
    }

    [TestMethod]
    public void BestTarget_prefers_id_then_role_then_css_then_xpath()
    {
        Assert.AreEqual("#save", new BrowserElementInfo { Found = true, Id = "save", Css = "x", Xpath = "y" }.BestTarget().Value);

        BrowserTarget role = new BrowserElementInfo { Found = true, Role = "button", Name = "Save", Css = "x", Xpath = "y" }.BestTarget();
        Assert.AreEqual(BrowserTargetKind.Role, role.Kind);

        Assert.AreEqual(BrowserTargetKind.Css, new BrowserElementInfo { Found = true, Css = "div.row", Xpath = "y" }.BestTarget().Kind);
        Assert.AreEqual(BrowserTargetKind.Xpath, new BrowserElementInfo { Found = true, Xpath = "/html/body" }.BestTarget().Kind);
    }
}
