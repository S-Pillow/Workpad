# Bug Fixes - Three Critical Issues Resolved

## Summary
Fixed three bugs related to text tokenization, event subscription leaks, and URL detection logic.

---

## Bug 1: SpellCheckService Offset Mismatch
**File:** `WorkNotes/Services/SpellCheckService.cs` (lines 191-204)

### Problem
When tokenizing text, quoted words like `'hello'` had quotes trimmed from the `Word` property but the `StartOffset` and `EndOffset` remained pointing to the original match range **including quotes**. This created a mismatch where:
- Word content: `"hello"` (5 characters)
- Offset range: points to `'hello'` (7 characters)

This caused incorrect text positioning if callers used these offsets to locate or manipulate words in the original text.

### Root Cause
```csharp
var word = match.Value;
word = word.Trim('\'');  // Trims quotes from word
tokens.Add(new TokenInfo
{
    Word = word,              // Trimmed word
    StartOffset = startOffset,  // Original offset (includes quotes!)
    EndOffset = endOffset       // Original offset (includes quotes!)
});
```

### Fix
Calculate adjusted offsets when quotes are trimmed:

```csharp
var word = match.Value;
var trimmedWord = word.Trim('\'');

// Adjust offsets if quotes were trimmed
var startQuotes = word.Length - word.TrimStart('\'').Length;
var endQuotes = word.Length - word.TrimEnd('\'').Length;

var adjustedStartOffset = startOffset + startQuotes;
var adjustedEndOffset = endOffset - endQuotes;

tokens.Add(new TokenInfo
{
    Word = trimmedWord,
    StartOffset = adjustedStartOffset,  // Now matches trimmed word
    EndOffset = adjustedEndOffset       // Now matches trimmed word
});
```

### Impact
- ✅ Spellcheck underlines now position correctly
- ✅ Word replacements happen at the right location
- ✅ Offset ranges accurately reflect the actual word content

---

## Bug 2: DocumentTab Event Subscription Leak
**File:** `WorkNotes/Models/DocumentTab.cs` (lines 19-50)

### Problem
When the `Document` property setter was called:
1. The event handler subscribed to the **old** document was **never unsubscribed**
2. The **new** document **never got a handler subscribed**

This meant:
- If a document was replaced, the tab header would **not update** when the new document changes
- The old document's changes would **still trigger updates** (memory leak)
- The old document couldn't be garbage collected (held by the lambda closure)

### Root Cause
```csharp
// Constructor: creates anonymous lambda (can't be unsubscribed later)
_document.PropertyChanged += (s, e) => { ... };

// Setter: replaces document but doesn't manage subscriptions
public Document Document
{
    set
    {
        _document = value;  // Old handler still attached to old doc!
        // No subscription to new document!
    }
}
```

### Fix
Store the handler as a field so it can be unsubscribed/resubscribed:

```csharp
private PropertyChangedEventHandler? _documentChangeHandler;

// Constructor
_documentChangeHandler = (s, e) => { ... };
_document.PropertyChanged += _documentChangeHandler;

// Setter
public Document Document
{
    set
    {
        // Unsubscribe from old document
        if (_document != null && _documentChangeHandler != null)
        {
            _document.PropertyChanged -= _documentChangeHandler;
        }

        _document = value;
        
        // Subscribe to new document
        if (_document != null && _documentChangeHandler != null)
        {
            _document.PropertyChanged += _documentChangeHandler;
        }
    }
}
```

### Impact
- ✅ No memory leaks from orphaned event handlers
- ✅ Tab header updates correctly when document is replaced
- ✅ Old documents can be garbage collected properly
- ✅ Only the current document's changes trigger UI updates

---

## Bug 3: MarkdownSerializer URL Detection Too Loose
**File:** `WorkNotes/Services/MarkdownSerializer.cs` (lines 83-86)

### Problem
The third condition in bare URL detection used `StartsWith` to check if a URL is bare:

```csharp
url.Replace("https://", "")
   .Replace("http://", "")
   .TrimEnd('/')
   .StartsWith(linkText.TrimEnd('/'))
```

This was **too loose** and incorrectly matched cases where the link text is a **prefix** of the URL.

**Example:**
- Link text: `"example"`
- URL: `"https://example.com"`
- URL without protocol: `"example.com"`
- `"example.com".StartsWith("example")` → **TRUE** ✗

Result: A labeled link `[example](https://example.com)` would be incorrectly serialized as just `example` (losing the label and URL).

### Root Cause
The logic assumed that if the URL starts with the link text, they're the same. But `"example.com"` starts with `"example"` even though they're not equal.

### Fix
Use **exact equality** checks instead of `StartsWith`:

```csharp
var urlWithoutProtocol = url.Replace("https://", "")
                            .Replace("http://", "")
                            .TrimEnd('/');
var linkTextTrimmed = linkText.TrimEnd('/');

if (linkText == url ||          // Exact match with protocol
    linkTextTrimmed == urlWithoutProtocol)  // Exact match without protocol
{
    // Bare URL
    sb.Append(linkText);
}
else
{
    // Labeled link
    sb.Append($"[{linkText}]({url})");
}
```

### Impact
- ✅ Labeled links with text that's a prefix of the URL serialize correctly
- ✅ `[example](https://example.com)` stays as `[example](https://example.com)`
- ✅ True bare URLs like `example.com` → `https://example.com` still work
- ✅ No loss of link information when round-tripping markdown

---

## Testing

All three bugs are now fixed and the application builds successfully:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Verification Steps

**Bug 1 - Tokenization:**
1. Type a word with quotes: `'test'`
2. Spellcheck should underline correctly
3. Right-click suggestions should replace the right text

**Bug 2 - Event Subscription:**
1. Create a tab with a document
2. Replace the document using the setter
3. Make changes to the new document
4. Tab header should update with dirty indicator (•)
5. Old document should not trigger any updates

**Bug 3 - URL Serialization:**
1. Create a link: `[example](https://example.com)`
2. Switch between Formatted/Source views
3. Link should remain as `[example](https://example.com)`
4. Not incorrectly simplified to just `example`

---

## Files Modified

1. `WorkNotes/Services/SpellCheckService.cs` - Fixed offset calculation when trimming quotes
2. `WorkNotes/Models/DocumentTab.cs` - Fixed event subscription/unsubscription
3. `WorkNotes/Services/MarkdownSerializer.cs` - Fixed bare URL detection logic
4. `WorkNotes/App.xaml.cs` - Made SpellCheckService a singleton
5. `WorkNotes/Controls/EditorControl.xaml.cs` - Use shared SpellCheckService instance
6. `WorkNotes/MainWindow.xaml.cs` - Use shared SpellCheckService instance, handle CustomDictionary changes
7. `WorkNotes/Dialogs/SettingsWindow.xaml.cs` - Fire CustomDictionary change event
8. `WorkNotes/Models/AppSettings.cs` - Made OnSettingChanged public for external triggering
9. `WorkNotes/Models/Document.cs` - Fixed dirty indicator consistency (asterisk → bullet)
10. `WorkNotes/Services/BionicReadingProcessor.cs` - Fixed single-character word bionic rendering, underscore tokenization

---

## Bug 5: Inconsistent Dirty Indicator
**File:** `WorkNotes/Models/Document.cs`

### Problem
`Document.DisplayName` used an asterisk `" *"` while `DocumentTab.HeaderText` used a bullet `" •"` for the dirty indicator. Users saw different indicators in the window title vs. tab headers for the same dirty state.

### Root Cause
Line 46 in `Document.cs`:
```csharp
public string DisplayName => FileName + (IsDirty ? " *" : string.Empty);
```

### Fix
Changed to use bullet character for consistency:
```csharp
public string DisplayName => FileName + (IsDirty ? " •" : string.Empty);
```

### Impact
- ✅ Visual consistency across window title and tab headers
- ✅ Matches intended design specification (bullet character)
- ✅ Better UX - single visual language

---

## Bug 6: Bionic Reading Single-Character Words Not Rendered
**File:** `WorkNotes/Services/BionicReadingProcessor.cs`

### Problem
Single-character words (e.g., "I", "a") were not receiving bionic styling:
1. The condition `boldLength > 0 && boldLength < token.Length` fails for 1-character words
2. For 1-char words: `boldLength < token.Length` is `0 < 1` = true, BUT...
3. In Strong mode, `CalculateBoldLength(1, Strong)` returns 0 (from `Math.Min(0, ...)`), failing the `boldLength > 0` check

Result: Single-character words were rendered without any bionic effect.

### Root Cause
Line 143 condition didn't handle single-character case:
```csharp
if (boldLength > 0 && boldLength < token.Length)
```

For a 1-character word:
- Light: `Math.Max(1, 0)` = 1, then `1 < 1` = false → skipped
- Medium: `Math.Ceil(0.5)` = 1, then `1 < 1` = false → skipped  
- Strong: `Math.Min(0, 1)` = 0, then `0 > 0` = false → skipped

### Fix
Added special handling for single-character words (lines 143-153):
```csharp
// For single-character words, bold the entire character
if (token.Length == 1)
{
    result.Add(new Run(token)
    {
        FontWeight = FontWeights.Bold,
        FontStyle = baseStyle,
        Foreground = run.Foreground,
        Background = run.Background
    });
}
else if (boldLength > 0 && boldLength < token.Length)
{
    // Original multi-character logic
}
```

### Impact
- ✅ Single-character words now bolded entirely (consistent with bionic reading principles)
- ✅ All strength modes (Light/Medium/Strong) handle 1-char words
- ✅ Better readability for common single-letter words ("I", "a")
- ✅ No regression for multi-character words

---

## Bug 7: Bionic Reading Text With Underscores Disappears
**File:** `WorkNotes/Services/BionicReadingProcessor.cs`

### Problem
Text containing underscores (e.g., "test_variable", "my_function_name") completely disappeared when bionic reading mode was enabled. The tokenization regex failed to capture any tokens, resulting in an empty list and the text vanishing from the view.

### Root Cause
Line 131 regex pattern:
```csharp
var tokens = Regex.Matches(text, @"(\b[a-zA-Z]+\b|[^\w]+|\b\d+\w*\b)");
```

The pattern had three fundamental issues with underscores:

1. **Underscores are word characters** (`\w` includes `[a-zA-Z0-9_]`), so they don't match `[^\w]+`
2. **No word boundary between letters and underscores** - `\b` only occurs at transitions between `\w` and `\W` (non-word chars). Since underscore is `\w`, there's no boundary in "test_variable"
3. **Entire string fails to match** - For "test_variable":
   - Can't match `\b[a-zA-Z]+\b` because there's no word boundary after "test" (underscore is `\w`)
   - Can't match `[^\w]+` because underscore is `\w`
   - Result: Zero tokens → empty list → text disappears

### Example Failures
- "test_variable" → No matches → Text disappears
- "my_function" → No matches → Text disappears  
- "snake_case_naming" → No matches → Text disappears
- "normal text" → Works (no underscores)

### Fix
Updated regex to explicitly handle underscores as separators (line 131-132):
```csharp
// Split into words and non-words (official standard: all alphabetic words)
// Updated to handle underscores: treat them as separators like spaces
var tokens = Regex.Matches(text, @"([a-zA-Z]+|\d+|[^\w]|\s+|_)");
```

New pattern behavior:
- `[a-zA-Z]+` - Captures letter sequences
- `\d+` - Captures digit sequences
- `[^\w]` - Captures non-word characters (punctuation, etc.)
- `\s+` - Captures whitespace
- `_` - Captures underscores explicitly as separate tokens

Now "test_variable" tokenizes as: `["test", "_", "variable"]`

### Impact
- ✅ Text with underscores no longer disappears
- ✅ Variable names, function names, and snake_case text display correctly
- ✅ Underscores treated as separators (not styled, just preserved)
- ✅ Words on both sides of underscores receive bionic styling
- ✅ No regression for normal text without underscores

---

## Bug 4: SpellCheckService Singleton Issue (Additional Fix)
**Files:** `WorkNotes/App.xaml.cs`, `WorkNotes/MainWindow.xaml.cs`, `WorkNotes/Controls/EditorControl.xaml.cs`, `WorkNotes/Dialogs/SettingsWindow.xaml.cs`

### Problem
The `SettingsWindow` received a **new** `SpellCheckService` instance created in `Preferences_Click`, while `EditorControl` maintained its own **separate instance**. When users added/removed words from the custom dictionary in Settings:
- Changes updated the Settings instance's in-memory dictionary ✓
- Changes saved to disk ✓
- But the active `EditorControl` instances had **stale data** in memory ✗

Result: Changes to custom dictionary didn't affect spell checking until app restart, breaking the "live updates" promise.

### Root Cause
```csharp
// App creates no shared instance
// EditorControl: new SpellCheckService()  ← Instance 1
// SettingsWindow: new SpellCheckService() ← Instance 2
```

Each instance loads from disk initially, but in-memory changes aren't shared.

### Fix
Made `SpellCheckService` a **singleton** accessible via `App.SpellCheckService`:

```csharp
// App.xaml.cs
public static SpellCheckService? SpellCheckService { get; private set; }

private void Application_Startup(...)
{
    SpellCheckService = new SpellCheckService();
}

// EditorControl.xaml.cs
_spellCheckService = App.SpellCheckService ?? throw new Exception(...);

// MainWindow.xaml.cs
var dialog = new SettingsWindow(App.SpellCheckService);
```

Additionally:
- Made `AppSettings.OnSettingChanged()` public
- SettingsWindow calls `OnSettingChanged("CustomDictionary")` after add/remove
- MainWindow listens for "CustomDictionary" changes and calls `RefreshSpellCheck()` on all tabs

### Impact
- ✅ All components share the same SpellCheckService instance
- ✅ Custom dictionary changes apply immediately to all open editors
- ✅ Add/remove word triggers live spell check refresh
- ✅ No app restart needed for dictionary changes to take effect

---

## Bug 8: Bionic Reading Corrupts Markdown on Save (CRITICAL) - FIXED
**File:** `WorkNotes/Controls/EditorControl.xaml.cs`

### Problem
When bionic reading mode was enabled in Formatted view, toggling it on/off or saving the document **permanently corrupted the stored markdown**. The FlowDocument was modified with split Run elements and bold formatting for bionic effect, then serialized back to markdown, injecting formatting markers between every tiny token.

**Example corruption:**
- Original: "test_variable is important"
- After bionic toggle: "**t**est**_**variabl**e** **i**s **importan**t"

This was a **data loss bug** - the original markdown was irreversibly corrupted.

### Root Cause
The fundamental architectural flaw was attempting to serialize a bionic-modified FlowDocument back to markdown:

```csharp
// Old broken approach - Line 323, 340, etc.
var markdownText = _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
```

The flow:
1. User enables bionic reading
2. `BionicReadingProcessor.ApplyBionicReading()` splits text into small runs with bold formatting
3. FlowDocument now contains: `[Run "t" Bold][Run "est" Normal][Run "_" Normal][Run "v" Bold]...`
4. On save/sync, `SerializeToMarkdown()` sees these as real formatting → outputs `**t**est_**v**...`
5. **Original markdown permanently corrupted**

**Why it happened:**
Bionic reading was **modifying the document structure** (splitting runs, adding FontWeight.Bold) but was intended to be a **view-only transformation**. The serializer couldn't distinguish between:
- User-intended markdown bold (`**user typed this**`)
- Bionic reading bold (`[Run "w" FontWeight=Bold]` from bionic split)

Additionally, when the user edited in Formatted view with bionic enabled, they were editing the bionic-split document, making corruption inevitable on any sync or save operation.

### Fix (Proper Architecture)
Implemented a **read-only bionic view** with **canonical source** pattern:

**1. Make Formatted view read-only when bionic is enabled:**
```csharp
private void UpdateFormattedEditorState()
{
    // When bionic is enabled in formatted view, make it read-only to prevent corruption
    if (_viewMode == EditorViewMode.Formatted)
    {
        FormattedEditor.IsReadOnly = App.Settings.EnableBionicReading;
    }
}
```

**2. Always use SourceEditor.Text as canonical source:**
```csharp
public void SaveToDocument()
{
    if (_document != null)
    {
        // CRITICAL: Always save from SourceEditor (canonical source of truth)
        if (_viewMode == EditorViewMode.Formatted && _markdownSerializer != null)
        {
            // Sync formatted → source first (but only if bionic is OFF)
            if (!App.Settings.EnableBionicReading)
            {
                var markdownText = _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
                _isSyncing = true;
                SourceEditor.Text = markdownText;
                _isSyncing = false;
            }
            
            // Always save from SourceEditor (canonical)
            _document.Save(SourceEditor.Text);
        }
        else
        {
            _document.Save(SourceEditor.Text);
        }
    }
}

public string GetText()
{
    // Always return from SourceEditor (canonical source of truth)
    return SourceEditor.Text;
}
```

**3. Always build Formatted view from canonical source:**
```csharp
public void RefreshBionicReading()
{
    UpdateFormattedEditorState();
    
    if (_viewMode == EditorViewMode.Formatted && _markdownParser != null)
    {
        // Use SourceEditor.Text as canonical source (not FormattedEditor.Document)
        var markdownText = SourceEditor.Text;
        var flowDoc = _markdownParser.ParseToFlowDocument(markdownText);
        
        // Apply bionic reading if enabled
        if (App.Settings.EnableBionicReading)
        {
            BionicReadingProcessor.ApplyBionicReading(flowDoc, App.Settings.BionicStrength);
        }
        
        _isSyncing = true;
        FormattedEditor.Document = flowDoc;
        _isSyncing = false;
    }
}
```

**4. Only sync Formatted → Source when bionic is OFF:**
```csharp
private void FormattedEditor_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_isLoading || _isSyncing || _document == null)
        return;

    // If bionic is enabled, formatted view is read-only, so user edits don't happen
    if (_viewMode == EditorViewMode.Formatted && !App.Settings.EnableBionicReading)
    {
        // Sync formatted -> source (only when bionic is OFF and user can edit)
        if (_markdownSerializer != null)
        {
            var markdownText = _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
            _isSyncing = true;
            SourceEditor.Text = markdownText;
            _isSyncing = false;
        }
        // ... link detection, spell check ...
    }
}
```

### Architecture Summary
**Before (Broken):**
- Formatted view editable with bionic ON → user edits bionic-split document
- Save/sync serializes bionic-modified FlowDocument → injects `**` markers
- Canonical source unclear, data loss inevitable

**After (Fixed):**
- **Canonical source:** SourceEditor.Text (always)
- **Bionic ON:** Formatted view is READ-ONLY, built from SourceEditor.Text
- **Bionic OFF:** Formatted view is EDITABLE, syncs back to SourceEditor.Text
- **Save/GetText:** Always uses SourceEditor.Text (never serializes bionic-modified document)
- **View switching:** Always builds Formatted from SourceEditor.Text

### Impact
- ✅ **Data integrity preserved** - original markdown never corrupted
- ✅ Bionic reading is strictly view-only (read-only surface when enabled)
- ✅ Toggling bionic on/off is safe and reversible
- ✅ Saving with bionic enabled preserves original formatting
- ✅ User-intended markdown formatting vs. bionic formatting now distinguished
- ✅ No data loss from bionic mode usage
- ✅ Clear separation: SourceEditor = canonical data, FormattedEditor = rendered view
- ✅ User must switch to Source view or disable bionic to edit (intentional limitation)

### Files Changed
1. `WorkNotes/Controls/EditorControl.xaml.cs` - Complete rewrite of bionic/save/sync architecture
   - Added `UpdateFormattedEditorState()` method
   - Modified `SaveToDocument()` to always use SourceEditor.Text
   - Modified `GetText()` to always use SourceEditor.Text
   - Modified `RefreshBionicReading()` to use SourceEditor.Text as source
   - Modified `SwitchViewMode()` to update read-only state and use canonical source
   - Modified `FormattedEditor_TextChanged()` to only sync when bionic is OFF
2. `WorkNotes/BUG_FIXES.md` - Updated documentation

---
