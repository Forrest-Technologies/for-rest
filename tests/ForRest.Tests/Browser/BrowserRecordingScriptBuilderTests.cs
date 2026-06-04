using ForRest.Browser;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class BrowserRecordingScriptBuilderTests
{
    [TestMethod]
    public void Navigation_emits_navigate_line()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddNavigation("https://example.com");

        string script = builder.Build();
        StringAssert.Contains(script, "await browser.navigate(\"https://example.com\")");
    }

    [TestMethod]
    public void Consecutive_duplicate_navigations_are_collapsed()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddNavigation("https://example.com");
        builder.AddNavigation("https://example.com");

        Assert.AreEqual(1, builder.LineCount);
    }

    [TestMethod]
    public void Click_event_emits_click_line_with_id_selector()
    {
        BrowserRecordingScriptBuilder builder = new();
        bool added = builder.AddEventJson("{\"type\":\"click\",\"selectorKind\":\"css\",\"selector\":\"#submit\"}");

        Assert.IsTrue(added);
        StringAssert.Contains(builder.Build(), "await browser.click(\"#submit\")");
    }

    [TestMethod]
    public void Css_selector_with_path_is_prefixed_with_css()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddEventJson("{\"type\":\"click\",\"selectorKind\":\"css\",\"selector\":\"div.row > button\"}");

        StringAssert.Contains(builder.Build(), "await browser.click(\"css=div.row > button\")");
    }

    [TestMethod]
    public void Xpath_selector_is_prefixed_with_xpath()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddEventJson("{\"type\":\"click\",\"selectorKind\":\"xpath\",\"selector\":\"/html/body/div[2]\"}");

        StringAssert.Contains(builder.Build(), "await browser.click(\"xpath=/html/body/div[2]\")");
    }

    [TestMethod]
    public void Type_events_to_same_field_collapse_to_latest_value()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddEventJson("{\"type\":\"type\",\"selectorKind\":\"css\",\"selector\":\"#name\",\"value\":\"Al\"}");
        builder.AddEventJson("{\"type\":\"type\",\"selectorKind\":\"css\",\"selector\":\"#name\",\"value\":\"Alice\"}");

        string script = builder.Build();
        Assert.AreEqual(1, builder.LineCount);
        StringAssert.Contains(script, "await browser.type(\"#name\", \"Alice\")");
    }

    [TestMethod]
    public void Click_between_types_starts_a_new_type_line()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddEventJson("{\"type\":\"type\",\"selectorKind\":\"css\",\"selector\":\"#name\",\"value\":\"Alice\"}");
        builder.AddEventJson("{\"type\":\"click\",\"selectorKind\":\"css\",\"selector\":\"#next\"}");
        builder.AddEventJson("{\"type\":\"type\",\"selectorKind\":\"css\",\"selector\":\"#name\",\"value\":\"Bob\"}");

        Assert.AreEqual(3, builder.LineCount);
    }

    [TestMethod]
    public void Select_event_emits_select_line()
    {
        BrowserRecordingScriptBuilder builder = new();
        builder.AddEventJson("{\"type\":\"select\",\"selectorKind\":\"css\",\"selector\":\"#country\",\"value\":\"US\"}");

        StringAssert.Contains(builder.Build(), "await browser.select(\"#country\", \"US\")");
    }

    [TestMethod]
    public void Malformed_payload_is_ignored()
    {
        BrowserRecordingScriptBuilder builder = new();
        Assert.IsFalse(builder.AddEventJson("not json"));
        Assert.IsFalse(builder.AddEventJson("{\"type\":\"click\"}"));
        Assert.AreEqual(0, builder.LineCount);
    }

    [TestMethod]
    public void Build_includes_header_comment()
    {
        BrowserRecordingScriptBuilder builder = new();
        StringAssert.Contains(builder.Build(), "// Recorded browser automation flow.");
    }
}
