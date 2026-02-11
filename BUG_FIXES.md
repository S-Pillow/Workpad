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
