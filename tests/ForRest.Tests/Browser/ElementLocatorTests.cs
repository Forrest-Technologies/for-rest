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

    [TestMethod]
    public void HumanPath_ends_exactly_on_target_and_is_deterministic_with_seed()
    {
        CursorMotion motion = CursorMotion.Default with { Steps = 30, Seed = 7 };
        CursorPoint from = new(0, 0);
        CursorPoint to = new(400, 120);

        IReadOnlyList<CursorPoint> first = ElementLocator.HumanPath(from, to, motion);
        IReadOnlyList<CursorPoint> second = ElementLocator.HumanPath(from, to, motion);

        Assert.AreEqual(30, first.Count);
        Assert.AreEqual(to.X, first[^1].X, 0.0001);
        Assert.AreEqual(to.Y, first[^1].Y, 0.0001);
        // Same seed => identical replay.
        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
    }

    [TestMethod]
    public void HumanPath_curves_off_the_straight_line()
    {
        CursorMotion motion = CursorMotion.Default with { Steps = 20, Jitter = 0, Seed = 1 };
        IReadOnlyList<CursorPoint> path = ElementLocator.HumanPath(new CursorPoint(0, 0), new CursorPoint(200, 0), motion);

        // A straight ease would keep y at 0; the human arc must bow away from the line somewhere.
        Assert.IsTrue(path.Any(point => Math.Abs(point.Y) > 1), "human path should arc off the straight line");
        Assert.AreEqual(0, path[^1].Y, 0.0001, "but still land exactly on the target");
    }

    [TestMethod]
    public void HumanPath_instant_motion_is_a_single_jump()
    {
        IReadOnlyList<CursorPoint> path = ElementLocator.HumanPath(new CursorPoint(5, 5), new CursorPoint(99, 42), CursorMotion.Instant);

        Assert.AreEqual(1, path.Count);
        Assert.AreEqual(99, path[0].X, 0.0001);
    }

    [TestMethod]
    public void StepDelay_lingers_at_the_ends()
    {
        int mid = ElementLocator.StepDelay(10, 0.5);
        int start = ElementLocator.StepDelay(10, 0.02);
        Assert.IsTrue(start > mid, "cursor should move slower at the start than mid-travel");
        Assert.AreEqual(0, ElementLocator.StepDelay(0, 0.5));
    }

    [TestMethod]
    public void KeystrokeDelays_returns_one_delay_per_character_within_range_and_is_deterministic()
    {
        TypingCadence cadence = new() { MinDelayMs = 20, MaxDelayMs = 60, Seed = 7 };

        IReadOnlyList<int> first = ElementLocator.KeystrokeDelays("hi there", cadence);
        IReadOnlyList<int> second = ElementLocator.KeystrokeDelays("hi there", cadence);

        Assert.AreEqual("hi there".Length, first.Count);
        CollectionAssert.AreEqual(first.ToArray(), second.ToArray(), "a seeded cadence must reproduce identical timing");
        // The character after the space gets a longer hesitation, but every value stays sane (>= min).
        Assert.IsTrue(first.All(delay => delay >= 20), "no keystroke should be faster than the minimum");
    }

    [TestMethod]
    public void KeystrokeDelays_instant_cadence_has_no_pauses()
    {
        IReadOnlyList<int> delays = ElementLocator.KeystrokeDelays("abc", TypingCadence.Instant);

        Assert.AreEqual(3, delays.Count);
        Assert.IsTrue(delays.All(delay => delay == 0));
    }
}
