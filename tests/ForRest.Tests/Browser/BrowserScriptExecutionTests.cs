using ForRest.Browser;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class BrowserScriptExecutionTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("css", "#name", "")]
    [DataRow("xpath", "//*[@id='name']", "")]
    [DataRow("testid", "name", "")]
    [DataRow("text", "Name", "")]
    [DataRow("role", "textbox", "Name")]
    public async Task Generated_locator_executes_and_finds_an_element(string kind, string value, string name)
    {
        var transport = new ExecutingTransport();
        var driver = new JsBridgeBrowserDriver(transport);
        var target = kind switch
        {
            "xpath" => BrowserTarget.Xpath(value),
            "testid" => BrowserTarget.TestId(value),
            "text" => BrowserTarget.Text(value),
            "role" => BrowserTarget.Role(value, name),
            _ => BrowserTarget.Css(value),
        };

        var element = await driver.Query(target);

        Assert.IsTrue(element.Found);
        Assert.AreEqual("name", element.Id);
        Assert.AreEqual("#name", element.Css);
        Assert.AreEqual(60d, element.X);
        Assert.AreEqual("undefined", transport.Engine.Evaluate("typeof __frFind").ToString());
    }

    [TestMethod]
    public async Task Generated_snapshot_executes_and_returns_page_elements()
    {
        var driver = new JsBridgeBrowserDriver(new ExecutingTransport());

        var snapshot = await driver.Snapshot();

        Assert.AreEqual("https://fixture.test/", snapshot.Url);
        Assert.AreEqual("Browser fixture", snapshot.Title);
        Assert.HasCount(1, snapshot.Elements);
        Assert.AreEqual("textbox", snapshot.Elements[0].Role);
    }

    [TestMethod]
    public async Task Generated_interactions_execute_instead_of_only_matching_script_text()
    {
        var transport = new ExecutingTransport();
        var driver = new JsBridgeBrowserDriver(transport);
        var target = BrowserTarget.Css("#name");
        const string text = "quote \" and slash \\";

        await driver.Type(target, text, CursorMotion.Instant, TypingCadence.Instant);
        await driver.Click(target, CursorMotion.Instant);

        Assert.AreEqual(text, transport.Engine.Evaluate("fixture.value").ToString());
        Assert.AreEqual("name", transport.Engine.Evaluate("document.activeElement.id").ToString());
        Assert.IsTrue(transport.Engine.Evaluate("events.indexOf('input') >= 0 && events.indexOf('click') >= 0").AsBoolean());
        Assert.AreEqual("Name", await driver.GetText(target));
        Assert.AreEqual("Name", await driver.GetAttribute(target, "aria-label"));
        await driver.Select(target, "selected");
        Assert.AreEqual("selected", transport.Engine.Evaluate("fixture.value").ToString());
    }

    [TestMethod]
    public async Task Generated_locator_handles_missing_elements()
    {
        var driver = new JsBridgeBrowserDriver(new ExecutingTransport());

        var element = await driver.Query(BrowserTarget.Css("#missing"));

        Assert.IsFalse(element.Found);
    }

    #endregion

    #region Private Types

    private sealed class ExecutingTransport : IBrowserPageTransport
    {
        public Engine Engine { get; } = new();

        public bool IsAvailable => true;

        public ExecutingTransport()
        {
            Engine.Execute("""
                var events = [];
                var fixture = {
                  id: 'name', tagName: 'INPUT', nodeType: 1, textContent: 'Name', value: '',
                  children: [], parentElement: null, isContentEditable: false,
                  getAttribute: function(name) { return name === 'aria-label' ? 'Name' : null; },
                  getBoundingClientRect: function() { return {left:10,top:20,width:100,height:30}; },
                  scrollIntoView: function() {},
                  focus: function() { document.activeElement = this; },
                  dispatchEvent: function(event) { events.push(event.type); return true; }
                };
                var document = {
                  title: 'Browser fixture', activeElement: null,
                  querySelector: function(selector) { return selector.indexOf('missing') < 0 ? fixture : null; },
                  querySelectorAll: function() { return [fixture]; },
                  evaluate: function() { return {singleNodeValue:fixture}; }
                };
                var location = {href:'https://fixture.test/'};
                var CSS = {escape:function(value) {return value;}};
                var XPathResult = {FIRST_ORDERED_NODE_TYPE:9};
                var window = {};
                function Event(type) {this.type=type;}
                var MouseEvent=Event, KeyboardEvent=Event, InputEvent=Event;
                """);
        }

        public Task<string> Evaluate(string script, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Engine.Evaluate(script).ToString());
        }

        public Task Navigate(string url, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> CaptureScreenshot(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }

    #endregion
}
