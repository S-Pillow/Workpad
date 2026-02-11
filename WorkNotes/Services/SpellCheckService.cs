using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace WorkNotes.Services
{
    /// <summary>
    /// Spell check service using Hunspell that intelligently skips technical tokens.
    /// </summary>
    public class SpellCheckService
    {
        private readonly WordList _dictionary;
        private readonly HashSet<string> _userDictionary;
        private readonly string _userDictionaryPath;

        // Patterns to skip
        private static readonly Regex UrlPattern = new Regex(
            @"https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9][a-zA-Z0-9-]*\.[a-zA-Z]{2,}(?:/[^\s]*)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EmailPattern = new Regex(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            RegexOptions.Compiled);

        private static readonly Regex AlphanumericPattern = new Regex(
            @"^[a-zA-Z]+\d+|^\d+[a-zA-Z]+",
            RegexOptions.Compiled);

        public SpellCheckService()
        {
            // Load main dictionary - CreateFromFiles expects the .dic file path
            var dictionaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries", "en_US.dic");
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading dictionary from: {dictionaryPath}");
                _dictionary = WordList.CreateFromFiles(dictionaryPath);
                System.Diagnostics.Debug.WriteLine("Dictionary loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dictionary load failed: {ex}");
                throw new InvalidOperationException(
                    $"Failed to load spell check dictionary from {dictionaryPath}. Ensure en_US.aff and en_US.dic are present.", ex);
            }

            // Load user dictionary
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkNotes");
            Directory.CreateDirectory(appDataPath);
            _userDictionaryPath = Path.Combine(appDataPath, "user_dictionary.txt");

            _userDictionary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LoadUserDictionary();
        }

        /// <summary>
        /// Checks if a word is spelled correctly.
        /// </summary>
        public bool IsCorrect(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return true;

            // Skip if in user dictionary
            if (_userDictionary.Contains(word))
                return true;

            // Skip URLs
            if (UrlPattern.IsMatch(word))
                return true;

            // Skip emails
            if (EmailPattern.IsMatch(word))
                return true;

            // Skip mixed alphanumeric (abc123, 123abc)
            if (AlphanumericPattern.IsMatch(word))
                return true;

            // Skip all-caps words (likely acronyms)
            if (word.Length > 1 && word.All(char.IsUpper))
                return true;

            // Skip numbers
            if (word.All(char.IsDigit))
                return true;

            // Skip words with underscores (likely code/variables)
            if (word.Contains('_'))
                return true;

            // Check with Hunspell
            var isCorrect = _dictionary.Check(word);
            System.Diagnostics.Debug.WriteLine($"SpellCheck: '{word}' => {isCorrect}");
            return isCorrect;
        }

        /// <summary>
        /// Gets spelling suggestions for a misspelled word.
        /// </summary>
        public List<string> GetSuggestions(string word, int maxSuggestions = 10)
        {
            if (string.IsNullOrWhiteSpace(word))
                return new List<string>();

            var suggestions = _dictionary.Suggest(word);
            return suggestions.Take(maxSuggestions).ToList();
        }

        /// <summary>
        /// Adds a word to the user's personal dictionary.
        /// </summary>
        public void AddToUserDictionary(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;

            _userDictionary.Add(word);
            SaveUserDictionary();
        }

        /// <summary>
        /// Adds a word to the custom dictionary (alias for UI).
        /// </summary>
        public void AddToCustomDictionary(string word)
        {
            AddToUserDictionary(word);
        }

        /// <summary>
        /// Removes a word from the custom dictionary.
        /// </summary>
        public void RemoveFromCustomDictionary(string word)
        {
            if (_userDictionary.Remove(word))
            {
                SaveUserDictionary();
            }
        }

        /// <summary>
        /// Gets all custom dictionary words.
        /// </summary>
        public List<string> GetCustomWords()
        {
            return _userDictionary.ToList();
        }

        /// <summary>
        /// Tokenizes text into words, skipping URLs/domains/emails.
        /// </summary>
        public List<TokenInfo> TokenizeText(string text)
        {
            var tokens = new List<TokenInfo>();
            if (string.IsNullOrEmpty(text))
                return tokens;

            // Find all protected spans (URLs, emails)
            var protectedSpans = new List<(int start, int end)>();

            foreach (Match match in UrlPattern.Matches(text))
            {
                protectedSpans.Add((match.Index, match.Index + match.Length));
            }

            foreach (Match match in EmailPattern.Matches(text))
            {
                // Only add if not already in a URL
                if (!IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                {
                    protectedSpans.Add((match.Index, match.Index + match.Length));
                }
            }

            // Extract words
            var wordPattern = new Regex(@"\b[\w']+\b", RegexOptions.Compiled);
            foreach (Match match in wordPattern.Matches(text))
            {
                var startOffset = match.Index;
                var endOffset = match.Index + match.Length;

                // Skip if this word is inside a protected span
                if (IsInProtectedSpan(startOffset, endOffset, protectedSpans))
                    continue;

                var word = match.Value;

                // Skip single quotes at start/end ('word' -> word)
                var trimmedWord = word.Trim('\'');
                
                // Adjust offsets if quotes were trimmed
                var startQuotes = word.Length - word.TrimStart('\'').Length;
                var endQuotes = word.Length - word.TrimEnd('\'').Length;
                
                var adjustedStartOffset = startOffset + startQuotes;
                var adjustedEndOffset = endOffset - endQuotes;

                if (!string.IsNullOrWhiteSpace(trimmedWord))
                {
                    tokens.Add(new TokenInfo
                    {
                        Word = trimmedWord,
                        StartOffset = adjustedStartOffset,
                        EndOffset = adjustedEndOffset,
                        IsCorrect = IsCorrect(trimmedWord)
                    });
                }
            }

            return tokens;
        }

        private bool IsInProtectedSpan(int start, int end, List<(int start, int end)> protectedSpans)
        {
            foreach (var span in protectedSpans)
            {
                if ((start >= span.start && start < span.end) ||
                    (end > span.start && end <= span.end) ||
                    (start <= span.start && end >= span.end))
                {
                    return true;
                }
            }
            return false;
        }

        private void LoadUserDictionary()
        {
            try
            {
                if (File.Exists(_userDictionaryPath))
                {
                    var words = File.ReadAllLines(_userDictionaryPath);
                    foreach (var word in words)
                    {
                        if (!string.IsNullOrWhiteSpace(word))
                        {
                            _userDictionary.Add(word.Trim());
                        }
                    }
                }
            }
            catch
            {
                // Silently fail - not critical
            }
        }

        private void SaveUserDictionary()
        {
            try
            {
                File.WriteAllLines(_userDictionaryPath, _userDictionary.OrderBy(w => w));
            }
            catch
            {
                // Silently fail - not critical
            }
        }
    }

    /// <summary>
    /// Information about a tokenized word.
    /// </summary>
    public class TokenInfo
    {
        public string Word { get; set; } = string.Empty;
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public bool IsCorrect { get; set; }
    }
}
