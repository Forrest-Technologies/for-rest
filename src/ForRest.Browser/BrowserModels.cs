namespace ForRest.Browser;

/// <summary>Details about a located element, including the stable selectors the recorder can emit.</summary>
public sealed record BrowserElementInfo
{
    public bool Found { get; init; }

    public string Tag { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string Css { get; init; } = string.Empty;

    public string Xpath { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    /// <summary>The most stable target the recorder should prefer for this element.</summary>
    public BrowserTarget BestTarget()
    {
        if (!string.IsNullOrEmpty(Id))
        {
            return BrowserTarget.Css("#" + Id);
        }

        if (!string.IsNullOrEmpty(Role) && !string.IsNullOrEmpty(Name))
        {
            return BrowserTarget.Role(Role, Name);
        }

        if (!string.IsNullOrEmpty(Css))
        {
            return BrowserTarget.Css(Css);
        }

        return BrowserTarget.Xpath(Xpath);
    }
}

/// <summary>A lightweight snapshot of the page's interactive surface for an agent's "eyes".</summary>
public sealed record BrowserSnapshot
{
    public string Url { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<BrowserElementInfo> Elements { get; init; } = [];
}

/// <summary>
/// Controls how the synthetic cursor travels to a target so replayed automation looks human and the
/// visible red pointer animates. Tuned per action; replay never needs an LLM to drive it.
/// </summary>
public sealed record CursorMotion
{
    /// <summary>Number of interpolated points between the current and target position.</summary>
    public int Steps { get; init; } = 24;

    /// <summary>Delay between interpolated points in milliseconds.</summary>
    public int StepDelayMs { get; init; } = 8;

    /// <summary>Whether the visible cursor overlay should be shown while moving.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Smooth, human-like default motion.</summary>
    public static CursorMotion Default { get; } = new();

    /// <summary>A single instantaneous jump with no delay or visible animation; ideal for tests and fast replays.</summary>
    public static CursorMotion Instant { get; } = new() { Steps = 1, StepDelayMs = 0, Visible = false };
}

/// <summary>A single point along a cursor path.</summary>
public readonly record struct CursorPoint(double X, double Y);
