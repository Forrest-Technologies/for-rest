using System.Collections.Generic;
using System.Linq;

namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ForRestLanguageCatalogTests
{
    [TestMethod]
    public void GetEntries_exposes_supported_flow_and_request_topics()
    {
        IReadOnlyList<ForRestLanguageHelpEntry> entries = ForRestLanguageCatalog.GetEntries();

        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.Any(static entry => entry.Key == "request-send"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "stash"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "extract-regex"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "range-literal"));
        Assert.IsTrue(entries.Any(static entry => entry.Key == "logic-aliases"));
        Assert.AreEqual(entries.Count, entries.Select(static entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void BuildMonacoCatalogJson_serializes_labels_examples_and_completion_metadata()
    {
        string json = ForRestLanguageCatalog.BuildMonacoCatalogJson();

        StringAssert.Contains(json, "\"label\":\"request.send()\"");
        StringAssert.Contains(json, "\"label\":\"stash\"");
        StringAssert.Contains(json, "\"label\":\"expect ... regex\"");
        StringAssert.Contains(json, "\"label\":\"[0..9]\"");
        StringAssert.Contains(json, "\"kind\":\"Method\"");
        StringAssert.Contains(json, "\"example\":\"let attempts = [0..2]");
    }
}
