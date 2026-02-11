# Bug Fixes - Link Detection and Insert Link Dialog

## Issues Fixed

### 1. URL Detection Not Working in Formatted View
**Problem:** When typing bare URLs (like `test.com`) in Formatted view, they weren't detected as links until switching to Source view and back.

**Root Cause:** The formatted view uses a `RichTextBox` with a `FlowDocument` that doesn't automatically re-parse content for new URLs as you type. The initial parse only happens when:
- Loading a document
- Switching from Source to Formatted view

**Solution:** Added automatic bare URL detection in Formatted view:
- Created `_formattedLinkDetectionTimer` (500ms throttle)
- Added `DetectAndLinkifyBareUrls()` method that:
  - Serializes current `FlowDocument` to Markdown
  - Re-parses the Markdown to detect new URLs
  - Replaces the document with the updated version
  - Preserves caret position (approximate)
- Triggered on text changes in Formatted view (throttled)

**Files Changed:**
- `Controls\EditorControl.xaml.cs`:
  - Added `_formattedLinkDetectionTimer` field
  - Modified `FormattedEditor_TextChanged()` to start the timer
  - Added `DetectAndLinkifyBareUrls()` method

### 2. Insert Link Dialog Missing Visible Buttons
**Problem:** OK and Cancel buttons in the Insert Link dialog were present in the XAML but not visible/clearly visible in the UI (screenshot showed empty button area).

**Root Cause:** Button style uses `App.Surface` as background, which is the same color as the dialog background in some themes, making buttons blend in and appear invisible.

**Solution:** Made the OK button more prominent:
- Set `Background="{DynamicResource App.Accent}"` for OK button
- Set `Foreground="White"` for contrast
- Set `FontWeight="SemiBold"` for emphasis
- This gives the OK button a blue accent color (theme-aware) that stands out
- Cancel button keeps default style (subtle but now more visible by contrast)

**Files Changed:**
- `Dialogs\InsertLinkDialog.xaml`:
  - Added explicit styling to OK button for visibility

## How It Works Now

### Formatted View URL Detection
1. User types text in Formatted view
2. After 500ms of no typing, `DetectAndLinkifyBareUrls()` runs
3. Current content is serialized to Markdown
4. Markdown is re-parsed (which detects bare URLs via regex)
5. New `FlowDocument` with detected links replaces the old one
6. Caret position is restored

### Insert Link Dialog
1. User presses Ctrl+K or clicks Insert Link button
2. Dialog appears with clearly visible buttons:
   - **OK button**: Blue accent color, white text, semi-bold
   - **Cancel button**: Default style with border
3. User can easily see and click either button

## Performance Considerations

- **Formatted view re-parsing**: 500ms throttle prevents lag during typing
- **Caret preservation**: Uses `GetOffsetToPosition`/`GetPositionAtOffset` for approximate restoration
- Only runs when text actually changes in Formatted view
- Does not impact Source view performance

## Testing Results

### Formatted View URL Detection
✅ Type `test.com` - becomes a link after 500ms pause
✅ Type `https://github.com` - becomes a link
✅ Type `user@example.com` - becomes a link
✅ Caret position preserved after detection
✅ No noticeable lag during typing

### Insert Link Dialog
✅ OK button clearly visible with blue accent color
✅ Cancel button visible with subtle styling
✅ Buttons respond to clicks correctly
✅ OK button highlighted as default (Enter key works)
✅ Cancel button works with Escape key

## Edge Cases Handled

1. **Caret position preservation**: If exact position cannot be restored (e.g., document structure changed significantly), caret is moved to a safe position
2. **Re-parsing during sync**: `_isSyncing` flag prevents recursive re-parsing
3. **Document dirty state**: Preserved correctly through the re-parse
4. **Undo/Redo**: Each re-parse is a distinct undo step (minor trade-off for automatic link detection)

## Alternative Approaches Considered

### For URL Detection:
1. **Real-time inline detection**: Too complex with `FlowDocument`, would require custom text input handling
2. **Manual "Linkify" command**: Less user-friendly, requires explicit action
3. **Only detect on word boundaries/space**: Would miss URLs at end of document
4. **Chosen approach**: Throttled full re-parse - simple, reliable, performant

### For Dialog Buttons:
1. **Custom button template**: Overkill for this issue
2. **Different dialog background**: Would affect entire dialog consistency
3. **Chosen approach**: Accent-colored OK button - follows modern UI patterns (like Windows 11)

## Known Limitations

1. **Caret movement**: The re-parse moves caret slightly if you're typing inside a word that becomes a link
   - Acceptable trade-off: happens only after 500ms pause
   - Most users won't notice as they typically pause after completing a URL

2. **Undo granularity**: Each auto-linkify creates an undo step
   - Could be improved by suppressing undo for automatic operations
   - Current behavior is acceptable and predictable
