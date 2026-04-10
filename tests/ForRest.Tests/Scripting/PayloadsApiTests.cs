using System.Collections.Generic;
using ForRest.Scripting;

namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class PayloadsApiTests
{
    [TestMethod]
    public void Builtin_categories_are_non_empty_and_unique_within_each_category()
    {
        PayloadsApi payloads = new();

        IReadOnlyList<IReadOnlyList<string>> builtins =
        [
            payloads.Sqli,
            payloads.Xss,
            payloads.PathTraversal,
            payloads.CommandInjection,
            payloads.Ssti,
            payloads.OpenRedirect,
            payloads.Xxe,
            payloads.NoSqli,
            payloads.CrlfInjection,
            payloads.Ssrf,
        ];

        foreach (IReadOnlyList<string> category in builtins)
        {
            Assert.IsTrue(category.Count > 0, "category must not be empty");
            HashSet<string> seen = new();
            foreach (string payload in category)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(payload));
                Assert.IsTrue(seen.Add(payload), $"duplicate payload: {payload}");
            }
        }
    }

    [TestMethod]
    public void Category_resolves_all_builtin_aliases()
    {
        PayloadsApi payloads = new();

        (string alias, int expectedMin)[] aliases =
        [
            ("sqli", 1),
            ("SQL", 1),
            ("sql_injection", 1),
            ("xss", 1),
            ("cross_site_scripting", 1),
            ("path_traversal", 1),
            ("lfi", 1),
            ("traversal", 1),
            ("command_injection", 1),
            ("cmdi", 1),
            ("rce", 1),
            ("ssti", 1),
            ("template_injection", 1),
            ("open_redirect", 1),
            ("redirect", 1),
            ("xxe", 1),
            ("xml_external_entity", 1),
            ("nosqli", 1),
            ("nosql", 1),
            ("crlf", 1),
            ("crlf-injection", 1),
            ("ssrf", 1),
        ];

        foreach ((string alias, int expectedMin) in aliases)
        {
            IReadOnlyList<string> result = payloads.Category(alias);
            Assert.IsTrue(result.Count >= expectedMin, $"alias '{alias}' resolved to {result.Count} payloads");
        }
    }

    [TestMethod]
    public void Category_returns_empty_list_for_unknown_category()
    {
        PayloadsApi payloads = new();

        IReadOnlyList<string> result = payloads.Category("totally-made-up");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Custom_categories_override_builtin_names_and_appear_in_Categories()
    {
        Dictionary<string, IReadOnlyList<string>> custom = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["sqli"] = new[] { "custom-override-only" },
            ["my-team-fuzz"] = new[] { "aaa", "bbb" },
        };

        PayloadsApi payloads = new(custom);

        CollectionAssert.AreEqual(new[] { "custom-override-only" }, (List<string>)new List<string>(payloads.Category("sqli")));
        Assert.AreEqual(2, payloads.Category("my-team-fuzz").Count);
        CollectionAssert.Contains(new List<string>(payloads.Categories()), "my-team-fuzz");
    }

    [TestMethod]
    public void Combine_dedupes_across_categories_and_literal_extras()
    {
        PayloadsApi payloads = new();

        IReadOnlyList<string> combined = payloads.Combine("sqli", "xss", "literal-extra", "sqli");

        Assert.IsTrue(combined.Count > payloads.Sqli.Count, "combined should include xss payloads too");
        CollectionAssert.Contains(new List<string>(combined), "literal-extra");
        HashSet<string> seen = new();
        foreach (string payload in combined)
        {
            Assert.IsTrue(seen.Add(payload), "Combine must not duplicate payloads");
        }
    }
}
