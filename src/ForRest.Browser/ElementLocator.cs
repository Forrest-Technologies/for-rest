namespace ForRest.Browser;

/// <summary>
/// Maps the JSON produced by the injected locate/snapshot scripts into typed results, and computes
/// the interpolated path the synthetic cursor follows. Pure functions, fully unit-testable.
/// </summary>
public static class ElementLocator
{
    #region Public Methods

    public static BrowserElementInfo FromJson(JsonNode? node)
    {
        if (node is null || node["found"]?.GetValue<bool>() != true)
        {
            return new BrowserElementInfo { Found = false };
        }

        return new BrowserElementInfo
        {
            Found = true,
            Tag = Str(node, "tag"),
            Id = Str(node, "id"),
            Text = Str(node, "text"),
            Css = Str(node, "css"),
            Xpath = Str(node, "xpath"),
            Role = Str(node, "role"),
            Name = Str(node, "name"),
            X = Num(node, "x"),
            Y = Num(node, "y"),
            Width = Num(node, "width"),
            Height = Num(node, "height"),
        };
    }

    public static BrowserSnapshot SnapshotFromJson(JsonNode? node)
    {
        if (node is null)
        {
            return new BrowserSnapshot();
        }

        List<BrowserElementInfo> elements = [];
        if (node["elements"] is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item is not null)
                {
                    elements.Add(FromJson(item));
                }
            }
        }

        return new BrowserSnapshot
        {
            Url = Str(node, "url"),
            Title = Str(node, "title"),
            Elements = elements,
        };
    }

    /// <summary>Interpolates a straight ease-in-out path between two points. The last point is exactly the target.</summary>
    public static IReadOnlyList<CursorPoint> Path(CursorPoint from, CursorPoint to, int steps)
    {
        int count = Math.Max(1, steps);
        List<CursorPoint> points = new(count);
        for (int i = 1; i <= count; i++)
        {
            double t = (double)i / count;
            double eased = EaseInOutCubic(t);
            points.Add(new CursorPoint(
                from.X + ((to.X - from.X) * eased),
                from.Y + ((to.Y - from.Y) * eased)));
        }

        return points;
    }

    /// <summary>
    /// Produces a human-feeling cursor path: a gently curved Bézier arc, eased acceleration and
    /// deceleration, a touch of mid-travel wobble, and a small overshoot that settles back onto the
    /// target. The final point is always exactly the target, and motion is deterministic when a seed
    /// is supplied (so recorded replays move identically).
    /// </summary>
    public static IReadOnlyList<CursorPoint> HumanPath(CursorPoint from, CursorPoint to, CursorMotion motion)
    {
        int count = Math.Max(1, motion.Steps);
        if (count == 1)
        {
            return [to];
        }

        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        Random random = motion.Seed is int seed ? new Random(seed) : new Random();

        // Perpendicular control point gives the arc; its side is stable per move.
        double perpX = distance > 0.001 ? -dy / distance : 0;
        double perpY = distance > 0.001 ? dx / distance : 0;
        double side = ((motion.Seed ?? (int)(from.X + from.Y + to.X + to.Y)) & 1) == 0 ? 1 : -1;
        double arc = motion.Curviness * distance * side;
        double controlX = (from.X + (dx / 2)) + (perpX * arc);
        double controlY = (from.Y + (dy / 2)) + (perpY * arc);

        double backStrength = motion.Overshoot * 20;
        List<CursorPoint> points = new(count);
        for (int i = 1; i <= count; i++)
        {
            double t = (double)i / count;
            double p = motion.Overshoot > 0 ? EaseOutBack(t, backStrength) : EaseInOutCubic(t);
            double inverse = 1 - p;

            double x = (inverse * inverse * from.X) + (2 * inverse * p * controlX) + (p * p * to.X);
            double y = (inverse * inverse * from.Y) + (2 * inverse * p * controlY) + (p * p * to.Y);

            // Wobble peaks mid-travel and vanishes at both ends so start and finish stay clean.
            double wobble = motion.Jitter * Math.Sin(Math.PI * t);
            if (i < count && wobble > 0)
            {
                x += (random.NextDouble() - 0.5) * 2 * wobble;
                y += (random.NextDouble() - 0.5) * 2 * wobble;
            }

            points.Add(i == count ? to : new CursorPoint(x, y));
        }

        return points;
    }

    /// <summary>Eased per-step delay so the cursor lingers at the start and end and hurries through the middle.</summary>
    public static int StepDelay(int baseDelayMs, double progress)
    {
        if (baseDelayMs <= 0)
        {
            return 0;
        }

        double factor = 1.35 - (0.7 * Math.Sin(Math.PI * Math.Clamp(progress, 0, 1)));
        return (int)Math.Round(baseDelayMs * factor);
    }

    #endregion

    #region Private Methods

    private static double EaseInOutCubic(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);

    private static double EaseOutBack(double t, double strength)
    {
        double u = t - 1;
        return 1 + ((strength + 1) * u * u * u) + (strength * u * u);
    }

    private static string Str(JsonNode node, string property)
    {
        JsonNode? value = node[property];
        return value is null ? string.Empty : value.GetValue<string>();
    }

    private static double Num(JsonNode node, string property)
    {
        JsonNode? value = node[property];
        return value is null ? 0 : value.GetValue<double>();
    }

    #endregion
}
