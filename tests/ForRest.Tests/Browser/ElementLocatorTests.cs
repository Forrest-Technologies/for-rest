using System.Collections.Generic;
using System.Text.Json.Nodes;
using ForRest.Browser;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class ElementLocatorTests
{
    [TestMethod]
    public void FromJson_maps_found_element()
    {
        JsonNode node = JsonNode.Parse(
            """
            {"found":true,"x":12.5,"y":24,"width":40,"height":20,"tag":"button","id":"save","text":"Save","css":"#save","xpath":"/html/body/button[1]","role":"button","name":"Save"}
            """)!;

        BrowserElementInfo info = ElementLocator.FromJson(node);

        Assert.IsTrue(info.Found);
        Assert.AreEqual(12.5, info.X);
        Assert.AreEqual("button", info.Tag);
        Assert.AreEqual("#save", info.Css);
        Assert.AreEqual("Save", info.Name);
    }

    [TestMethod]
    public void FromJson_handles_not_found_and_null()
    {
        Assert.IsFalse(ElementLocator.FromJson(JsonNode.Parse("{\"found\":false}")).Found);
        Assert.IsFalse(ElementLocator.FromJson(null).Found);
    }

    [TestMethod]
    public void SnapshotFromJson_reads_elements()
    {
        JsonNode node = JsonNode.Parse(
            """
            {"url":"https://x.test/","title":"Home","elements":[{"found":true,"tag":"a","x":1,"y":2,"width":3,"height":4},{"found":true,"tag":"button","x":5,"y":6,"width":7,"height":8}]}
            """)!;

        BrowserSnapshot snapshot = ElementLocator.SnapshotFromJson(node);

        Assert.AreEqual("https://x.test/", snapshot.Url);
        Assert.AreEqual("Home", snapshot.Title);
        Assert.AreEqual(2, snapshot.Elements.Count);
        Assert.AreEqual("button", snapshot.Elements[1].Tag);
    }

    [TestMethod]
    public void Path_interpolates_and_ends_at_target()
    {
        IReadOnlyList<CursorPoint> path = ElementLocator.Path(new CursorPoint(0, 0), new CursorPoint(100, 50), 10);

        Assert.AreEqual(10, path.Count);
        Assert.AreEqual(100, path[^1].X, 0.001);
        Assert.AreEqual(50, path[^1].Y, 0.001);
        Assert.IsTrue(path[0].X < path[^1].X, "path should progress toward the target");
    }

    [TestMethod]
    public void Path_clamps_step_count()
    {
        Assert.AreEqual(1, ElementLocator.Path(new CursorPoint(0, 0), new CursorPoint(10, 10), 0).Count);
    }
}
