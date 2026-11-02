using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkNotes.Services
{
    /// <summary>
    /// A named command surface: id, title, keywords for palette search, and
    /// the action. Commands are registered once and run from the palette or
    /// programmatically.
    /// </summary>
    public class CommandEntry
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public string Keywords { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Shortcut { get; init; } = string.Empty;
        public required Action Execute { get; init; }
        public Func<bool>? IsEnabled { get; init; }
    }

    /// <summary>Central registry used by the command palette (Ctrl+Shift+P).</summary>
    public class CommandRegistry
    {
        private readonly List<CommandEntry> _commands = new();

        public IReadOnlyList<CommandEntry> Commands => _commands;

        public void Register(CommandEntry entry) => _commands.Add(entry);

        public CommandEntry? Find(string id) => _commands.FirstOrDefault(c => c.Id == id);

        /// <summary>Filter for the palette: matches title, keywords, category.</summary>
        public IEnumerable<CommandEntry> Search(string query)
        {
            var enabled = _commands.Where(c => c.IsEnabled?.Invoke() != false);
            if (string.IsNullOrWhiteSpace(query))
                return enabled.OrderBy(c => c.Category).ThenBy(c => c.Title);
            var q = query.Trim();
            return enabled
                .Where(c => c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || c.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || c.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || MatchesFuzzy(c.Title, q))
                .OrderByDescending(c => c.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                .ThenBy(c => c.Title);
        }

        private static bool MatchesFuzzy(string title, string query)
        {
            var ti = 0;
            foreach (var qc in query.Where(c => !char.IsWhiteSpace(c)))
            {
                ti = title.IndexOf(qc, ti, StringComparison.OrdinalIgnoreCase);
                if (ti < 0) return false;
                ti++;
            }
            return true;
        }
    }
}
