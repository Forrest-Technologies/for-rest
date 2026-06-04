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

    /// <summary>Interpolates an ease-in-out path between two points so the visible cursor moves naturally.</summary>
    public static IReadOnlyList<CursorPoint> Path(CursorPoint from, CursorPoint to, int steps)
    {
        int count = Math.Max(1, steps);
        List<CursorPoint> points = new(count);
        for (int i = 1; i <= count; i++)
        {
            double t = (double)i / count;
            double eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            points.Add(new CursorPoint(
                from.X + ((to.X - from.X) * eased),
                from.Y + ((to.Y - from.Y) * eased)));
        }

        return points;
    }

    #endregion

    #region Private Methods

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
