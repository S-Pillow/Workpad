# Split View Implementation - Architecture Notes

## Industry Best Practice Architecture

### Shared Buffer Pattern (Source Mode)
Both editor panes reference the SAME `TextDocument` instance from AvalonEdit. This provides:
- **Perfect synchronization**: Changes in one pane instantly appear in the other
- **Shared undo stack**: Undo/redo works predictably across both panes
- **Zero lag**: No serialization/parsing overhead
- **Single source of truth**: The TextDocument IS the canonical data

### Projection Pattern (Formatted Mode)
Top pane is editable, bottom pane is read-only mirror:
- **Canonical source**: `Document.Content` (markdown string)
- **View projections**: Both FlowDocuments are rendered FROM the canonical source
- **One-way sync**: Top (editable) → Document.Content → Bottom (mirror)
- **Throttled updates**: 300ms delay prevents typing lag
- **Read-only mirror**: Prevents dual-edit RichText conflicts

### Command Routing
All commands target the **active (focused) pane**:
- `DocumentTab.GetActiveEditorControl()` returns the focused editor
- `EditorPane` tracks focus and shows visual active border
- Debug logging helps catch targeting bugs during development

### Files Added
1. `Services/ProFeatures.cs` - Feature gating system
2. `Controls/EditorPane.xaml` + `.cs` - Focus-tracking wrapper with active border
3. `Controls/SplitViewContainer.xaml` + `.cs` - Top/bottom split with GridSplitter
4. `Models/DocumentTab.cs` - Extended for split view support

### Files Modified
1. `MainWindow.xaml` - Added split view menu + toolbar button
2. `MainWindow.xaml.cs` - Updated command routing to use `GetActiveEditorControl()`
3. `Controls/EditorControl.xaml.cs` - Added split view helper methods

## Acceptance Tests

### Test 1: Source Mode Sync
- Type in top pane → bottom updates immediately ✓
- Shared undo stack works ✓
- Independent scroll positions ✓

### Test 2: Formatted Mode Projection
- Edit in top → bottom mirror updates (throttled) ✓
- Bottom pane is read-only ✓
- Canonical source preserved ✓

### Test 3: Command Targeting
- Bold/Italic/Link target focused pane only ✓
- Cut/Copy/Paste target focused pane only ✓
- Find/Replace opens for focused pane ✓

### Test 4: Pro Feature Gating
- Non-Pro user sees disabled UI with lock icon ✓
- Upgrade dialog appears on click ✓

### Test 5: Toggle Split View
- Enable split → two panes appear ✓
- Disable split → returns to single editor ✓
- Content preserved across toggle ✓

### Test 6: No Data Corruption
- Switching modes doesn't corrupt markdown ✓
- Save preserves original formatting ✓
- No bionic-style serialization artifacts ✓
