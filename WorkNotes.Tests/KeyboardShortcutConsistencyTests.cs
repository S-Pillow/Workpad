using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WorkNotes.Tests;

/// <summary>
/// Guards a bug class this app kept producing: the interface promising a keyboard
/// shortcut the app never actually binds. Ctrl+Plus / Ctrl+Minus / Ctrl+0 were
/// advertised in the status bar from v1.2 until v1.7.5 while only the buttons worked,
/// because nothing connected the label to the binding.
///
/// These tests read the shipped sources and assert that every shortcut the UI (or the
/// README) advertises is either bound with a KeyBinding or is a documented gesture the
/// framework provides for free. A promise with no binding behind it fails the build.
/// </summary>
public sealed class KeyboardShortcutConsistencyTests
{
    /// <summary>
    /// Gestures the app deliberately does not bind because WPF already provides them.
    /// The value is the reason, which is printed when a test fails so the exemption can
    /// be judged rather than merely trusted.
    /// </summary>
    private static readonly Dictionary<string, string> FrameworkProvided = new()
    {
        ["Ctrl+Z"] = "TextBoxBase / AvalonEdit built-in undo",
        ["Ctrl+Y"] = "TextBoxBase / AvalonEdit built-in redo",
        ["Ctrl+X"] = "ApplicationCommands.Cut, handled by the editor controls",
        ["Ctrl+C"] = "ApplicationCommands.Copy, handled by the editor controls",
        ["Ctrl+V"] = "ApplicationCommands.Paste, handled by the editor controls",
        ["Ctrl+A"] = "ApplicationCommands.SelectAll, handled by the editor controls",
        ["Alt+F4"] = "Windows system close command, not an application binding",
    };

    /// <summary>Mouse gestures that appear in docs but are not keyboard bindings.</summary>
    private static readonly HashSet<string> NotKeyboardGestures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+Click",
    };

    [Fact]
    public void EveryAdvertisedShortcutHasAMatchingKeyBinding()
    {
        var bound = ParseBoundGestures();
        var advertised = ParseAdvertisedGestures();

        // A parser that silently matches nothing would make this test pass vacuously.
        Assert.True(bound.Count >= 15, $"Only found {bound.Count} KeyBindings — the binding parser is probably broken.");
        Assert.True(advertised.Count >= 20, $"Only found {advertised.Count} advertised shortcuts — the advertisement parser is probably broken.");

        var unfulfilled = new List<string>();

        foreach (var (gesture, sources) in advertised.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (NotKeyboardGestures.Contains(gesture) || FrameworkProvided.ContainsKey(gesture))
                continue;

            var candidates = CandidateBindings(gesture);
            if (candidates.Any(bound.Contains))
                continue;

            unfulfilled.Add(
                $"  {gesture,-16} advertised in {string.Join(", ", sources.OrderBy(s => s, StringComparer.Ordinal))}" +
                $"{Environment.NewLine}                   expected one of: {string.Join(" | ", candidates)}");
        }

        Assert.True(unfulfilled.Count == 0,
            "The UI advertises keyboard shortcuts that are never bound with a KeyBinding:" + Environment.NewLine +
            string.Join(Environment.NewLine, unfulfilled) + Environment.NewLine + Environment.NewLine +
            "Either add the InputBinding in MainWindow.SetupKeyboardShortcuts, or stop advertising it. " +
            "If WPF provides the gesture natively, add it to FrameworkProvided with a reason." + Environment.NewLine +
            "Bindings found: " + string.Join(", ", bound.OrderBy(b => b, StringComparer.Ordinal)));
    }

    [Fact]
    public void ExemptedShortcutsAreActuallyAdvertisedSomewhere()
    {
        // Keeps the exemption list from rotting into a place where real bindings go to hide.
        var advertised = ParseAdvertisedGestures();
        var stale = FrameworkProvided.Keys
            .Where(g => !advertised.ContainsKey(g))
            .ToList();

        Assert.True(stale.Count == 0,
            "These gestures are exempted but no longer advertised anywhere — remove them from FrameworkProvided: " +
            string.Join(", ", stale));
    }

    // ---- source parsing -------------------------------------------------------------

    private static readonly Regex KeyBindingPattern = new(
        @"new\s+KeyBinding\(\s*[^,]+,\s*Key\.(?<key>[A-Za-z0-9]+)\s*,\s*(?<mods>ModifierKeys\.[A-Za-z]+(?:\s*\|\s*ModifierKeys\.[A-Za-z]+)*)\s*\)",
        RegexOptions.Compiled);

    /// <summary>Ctrl+1..9 are registered in a loop as <c>Key.D0 + tabIndex</c>.</summary>
    private static readonly Regex TabJumpLoopPattern = new(
        @"new\s+KeyBinding\(\s*[^,]+,\s*Key\.D0\s*\+\s*\w+\s*,\s*ModifierKeys\.Control\s*\)",
        RegexOptions.Compiled);

    private static HashSet<string> ParseBoundGestures()
    {
        var bound = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(file);

            foreach (Match m in KeyBindingPattern.Matches(text))
            {
                var mods = Regex.Matches(m.Groups["mods"].Value, @"ModifierKeys\.([A-Za-z]+)")
                                .Select(x => x.Groups[1].Value)
                                .Where(x => x != "None");
                bound.Add(Normalize(mods, m.Groups["key"].Value));
            }

            if (TabJumpLoopPattern.IsMatch(text))
            {
                for (var i = 1; i <= 9; i++)
                    bound.Add(Normalize(new[] { "Control" }, $"D{i}"));
            }
        }

        return bound;
    }

    private static readonly Regex GestureInParens = new(
        @"\((?<gesture>(?:Ctrl|Alt|Shift)\+[A-Za-z0-9+]+)\)", RegexOptions.Compiled);

    private static Dictionary<string, SortedSet<string>> ParseAdvertisedGestures()
    {
        var advertised = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string gesture, string source)
        {
            gesture = gesture.Trim();
            if (gesture.Length == 0) return;
            if (!advertised.TryGetValue(gesture, out var sources))
                advertised[gesture] = sources = new SortedSet<string>(StringComparer.Ordinal);
            sources.Add(source);
        }

        // Menu items: InputGestureText is the most literal promise the app makes.
        foreach (var file in SourceFiles("*.xaml"))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (Match m in Regex.Matches(text, @"InputGestureText\s*=\s*""(?<g>[^""]+)"""))
                Add(m.Groups["g"].Value, name);

            foreach (Match m in Regex.Matches(text, @"ToolTip\s*=\s*""(?<t>[^""]*)"""))
                foreach (Match g in GestureInParens.Matches(m.Groups["t"].Value))
                    Add(g.Groups["gesture"].Value, name);

            // Tooltips applied through a Style setter, e.g. the new-tab "+" button.
            foreach (Match m in Regex.Matches(text, @"Property\s*=\s*""ToolTip""\s+Value\s*=\s*""(?<t>[^""]*)"""))
                foreach (Match g in GestureInParens.Matches(m.Groups["t"].Value))
                    Add(g.Groups["gesture"].Value, name);
        }

        // Tooltips assigned from code-behind (e.g. the view-mode toggle).
        foreach (var file in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (Match m in Regex.Matches(text, @"\.ToolTip\s*=\s*""(?<t>[^""]*)"""))
                foreach (Match g in GestureInParens.Matches(m.Groups["t"].Value))
                    Add(g.Groups["gesture"].Value, name);
        }

        // The README shortcut table is a promise to users too.
        var readme = Path.Combine(RepoRoot(), "README.md");
        if (File.Exists(readme))
        {
            foreach (var line in File.ReadAllLines(readme))
            {
                var m = Regex.Match(line, @"^\|\s*(?<g>(?:Ctrl|Alt|Shift|F\d\d?)[^|]*?)\s*\|");
                if (!m.Success) continue;

                var gesture = m.Groups["g"].Value.Trim();
                if (gesture.Contains("..", StringComparison.Ordinal))
                {
                    // "Ctrl+1..9" — assert the whole documented range.
                    for (var i = 1; i <= 9; i++) Add($"Ctrl+{i}", "README.md");
                    continue;
                }

                Add(gesture, "README.md");
            }
        }

        return advertised;
    }

    // ---- gesture normalisation ------------------------------------------------------

    private static string Normalize(IEnumerable<string> modifiers, string key)
    {
        var mods = modifiers
            .Select(m => m switch { "Ctrl" => "Control", var other => other })
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal);

        return string.Join("+", mods.Append(key));
    }

    /// <summary>
    /// The bindings that would satisfy an advertised gesture. Several labels map to more
    /// than one physical key — Windows reports the number row and the numpad separately,
    /// so "Ctrl+0" is honoured by either D0 or NumPad0.
    /// </summary>
    private static IReadOnlyList<string> CandidateBindings(string gesture)
    {
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mods = parts.Take(parts.Length - 1);
        var key = parts[^1];

        var keys = key switch
        {
            "Plus" => new[] { "OemPlus", "Add" },
            "Minus" => new[] { "OemMinus", "Subtract" },
            _ when key.Length == 1 && char.IsDigit(key[0]) => new[] { $"D{key}", $"NumPad{key}" },
            _ => new[] { key },
        };

        return keys.Select(k => Normalize(mods, k)).ToList();
    }

    // ---- file discovery -------------------------------------------------------------

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "WorkNotes"), pattern, SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                          && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                 .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>
    /// Repository root, resolved from this file's compile-time path. These tests read the
    /// sources rather than the compiled assembly because what is being verified is the
    /// relationship between UI text and binding registration, which does not survive
    /// compilation in an inspectable form.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)
                      ?? throw new InvalidOperationException("Could not resolve the test source directory.");
        var root = Path.GetFullPath(Path.Combine(testDir, ".."));

        Assert.True(Directory.Exists(Path.Combine(root, "WorkNotes")),
            $"Expected the WorkNotes sources under '{root}'. These tests must run from a source checkout.");

        return root;
    }
}
