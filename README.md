# Work Notes

A modern, feature-rich Windows 11 note editor built with WPF, designed for productivity with URL/domain-heavy content.

## Features

### 📝 Dual View Mode
- **Formatted View** (default): Beautiful rendering with bold, italic, and clickable links
- **Source View**: Raw Markdown editing
- Seamless switching with Ctrl+Shift+M

### ✨ Rich Text Formatting
- **Bold** (Ctrl+B): `**text**`
- *Italic* (Ctrl+I): `*text*`
- **[Insert Link](url)** (Ctrl+K): Create labeled links
- Auto-link detection for bare URLs, domains, and emails
- Full context menu support in both views

### 🔍 Find & Replace
- Powerful find/replace with Ctrl+F / Ctrl+H
- Match case and whole word options
- Wrap-around search
- Replace All with single undo step
- Go to line with Ctrl+G
- Works perfectly in both Formatted and Source views

### 🔗 Smart Link Handling
- Clickable URLs, domains, and emails in Formatted view
- Ctrl+Click to open links
- Safety confirmation dialog before opening with security indicators
- Right-click context menu: "Open Link" / "Copy Link Address"
- Toggle auto-link detection in Settings
- Only opens safe http/https schemes

### ✍️ Spellcheck
- English dictionary with red underline for misspelled words
- Right-click for suggestions and instant corrections
- Custom user dictionary (Add to Dictionary)
- Intelligently skips URLs, domains, and emails
- Toggle on/off in Settings
- Works in both Formatted and Source views

### 👁️ Bionic Reading Mode
- Optional reading enhancement that bolds the first letters of words
- Three strength levels: Light, Medium, Strong
- Toggle on/off in View menu or Settings
- **Fully editable while active** — keep writing with the reading aid on; newly typed
  words are styled once you pause, and the caret holds its place
- Non-destructive: bionic bolding never leaks into your saved Markdown, so only your
  own `**bold**` survives a round-trip
- Intelligently skips URLs and technical tokens

### ↔️ Two-Document Split View
- Open two unrelated files side by side with a resizable divider
- Independent editor buffers, selections, undo history, navigation, and scrolling
- Open or replace either pane without changing the other
- Mark either pane as a read-only reference
- Close either pane and continue working in the remaining document

### 🎨 Modern UI & Polish
- **Custom Windows 11 Title Bar**: Native-looking caption buttons that adapt to themes,
  with native rounded window corners on Windows 11
- **Unified Header Shell**: Title bar, menu, and toolbar on one gently graded surface
- **Segmented Toolbar**: Icons grouped into raised pills (file / format / view) so the
  toolbar reads as clusters rather than a row of loose glyphs
- **Tab Cards**: Rounded pill tabs with a gradient accent indicator on the active tab,
  inline "+" button, and an unsaved dot that swaps to a close X on hover
- **Chip-based Status Bar**: View mode, caret position, unsaved state, zoom cluster and
  SPELL / FOCUS / SPLIT flags are all discrete, clickable chips
- **View Modes**: Full Screen (F11), Post-It (F12), Distraction Free (Ctrl+Shift+F), Always On Top
- Light/Dark/System theme modes with instant toggle
- Both palettes built on a three-step elevation ladder with a single indigo→violet→teal
  accent; every surface/text pairing is checked against WCAG AA contrast
- Comfortable editor padding and generous line height for long reading sessions
- "Start with a thought" empty state for new documents
- Zoom controls (50% - 300%)

### 📂 Advanced Tab Management
- Multiple open documents with Notepad-style tab strip
- Inline **+** button to create new tabs (always visible after last tab)
- Horizontal scroll overflow when many tabs are open
- Visual unsaved indicator dot (swaps with close X on hover)
- Close tab confirmation if unsaved
- Middle-click to close a tab
- Right-click a tab for **Close Others**, **Close Tabs to the Right**, **Close All**,
  **Copy File Path** and **Show in File Explorer**
- Double-click empty space on the tab strip to open a new tab
- Hover a tab to see its full file path
- **Recent Files** list (File menu)
- Open multiple Markdown/text files by dragging them into the window
- **Reopen Closed Tab** (Ctrl+Shift+T)
- **Restore Open Tabs** on startup (optional)
- Session persistence
- Standard shortcuts: Ctrl+T (new tab), Ctrl+W (close tab), Ctrl+O, Ctrl+S

### ⚙️ Comprehensive Settings
- Windows 11 Notepad-style Settings window
- **App Theme**: Light / Dark / System
- **Text Formatting**: Font family, font size, word wrap (defaults to Times New Roman 14pt)
- **Bionic Reading**: Enable/disable with strength presets
- **Spelling**: Toggle spellcheck, manage custom dictionary
- All changes apply immediately to all open tabs
- Settings persist across restarts

## Technology Stack

- **Framework**: .NET 8 / WPF (Windows Presentation Foundation)
- **Text Editor**: AvalonEdit (Source view) + RichTextBox (Formatted view)
- **Markdown**: Custom parser with bold, italic, and link support
- **Spellcheck**: Hunspell via WeCantSpell.Hunspell
- **Storage**: Plain UTF-8 .txt files

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+T | New Tab |
| Ctrl+N | New File |
| Ctrl+O | Open File |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+W | Close Tab |
| Ctrl+Shift+T | Reopen Closed Tab |
| Ctrl+B | Bold |
| Ctrl+I | Italic |
| Ctrl+K | Insert Link |
| Ctrl+F | Find |
| Ctrl+H | Replace |
| Ctrl+G | Go to line |
| Ctrl+Shift+M | Toggle View Mode |
| F11 | Full Screen |
| F12 | Post-It Mode |
| Ctrl+Shift+F | Distraction Free |
| Ctrl+Tab | Next Tab |
| Ctrl+Shift+Tab | Previous Tab |
| Ctrl+1..9 | Jump to Tab (9 = last) |
| Ctrl+Shift+Left | Move Tab Left |
| Ctrl+Shift+Right | Move Tab Right |
| Ctrl+Plus | Zoom In |
| Ctrl+Minus | Zoom Out |
| Ctrl+0 | Reset Zoom |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+Click | Open Link |

## Download & Install

**Portable (no install needed):** grab the latest `WorkNotes-<version>-win-x64.zip`
from [GitHub Releases](https://github.com/S-Pillow/Workpad/releases) (≈65 MB). The
package is self-contained — the .NET runtime is bundled, nothing to install.

The archive contains:
- `WorkNotes.exe` — the application
- `Dictionaries/` — spell check dictionary files (must stay alongside the exe)

### First run: the SmartScreen warning

Releases are **not code-signed**, so Windows Defender SmartScreen shows
*"Windows protected your PC"* the first time you run the app. That warning is about
the missing signature, not about anything the app does.

The warning is triggered by the "mark of the web" your browser stamps on the download.
Clear it before extracting and the prompt does not appear:

```powershell
Unblock-File "$env:USERPROFILE\Downloads\WorkNotes-v1.7.5-win-x64.zip"
```

Otherwise, click **More info → Run anyway**.

### Verifying your download

Each release ships a `.sha256` file next to the zip. Since the build is unsigned, this
hash is the only way to confirm you have exactly what CI produced:

```powershell
Get-FileHash .\WorkNotes-v1.7.5-win-x64.zip -Algorithm SHA256
```

## Requirements

- Windows 10/11 (x64)
- No additional runtime needed (self-contained build)

## Building from Source

```bash
dotnet build -c Release
```

### Publishing a Self-Contained Single-File Build

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/`

## Releasing

Releases are tag-driven. Pushing a `v*` tag builds that exact commit, runs the test
suite, and publishes a GitHub Release with the zipped executable and its SHA256
attached:

```bash
git tag -a v1.7.6 -m "Work Notes v1.7.6"
git push origin v1.7.6
```

Merging to `main` builds and tests but releases nothing, so `main` stays safe to push
to. See `.github/workflows/release.yml`.

## Running from Source

```bash
dotnet run --project WorkNotes
```

## License

This project is open source and available under the MIT License.

## Why Work Notes?

Built specifically for users who work with lots of URLs, domains, and technical content in their notes. Features like intelligent spellcheck (that skips domains), smart link handling with safety confirmations, bionic reading mode, and dual-view editing make it perfect for developers, sysadmins, and technical writers.

### Key Differentiators
- **Domain/URL-Aware**: Spellcheck and bionic reading skip technical tokens
- **Safety First**: Link confirmation dialogs prevent accidental navigation
- **Dual Representation**: True formatted view with markdown storage (not just syntax highlighting)
- **Session Management**: Remembers your open tabs and recently closed files
- **Productivity Focus**: Zoom, real-time line/column/word count tracking, comprehensive keyboard shortcuts
- **Reading Without Read-Only**: Bionic Reading stays fully editable, so a focus aid
  never stops you writing — and never contaminates the saved Markdown
- **Polish**: Custom Windows 11 chrome, gradient accent identity, contrast-audited
  palettes, consistent theming everywhere
- **Modern UX**: Chip-based status bar, segmented toolbar, comfortable editor padding

## Release History

### Version 1.7.5 (August 2026)
**2027 Visual Redesign, Editable Bionic Reading & Rendering Fixes**

*Interface*
- Rebuilt both theme palettes on a three-step elevation ladder over a blue-shifted
  near-black canvas, retiring the leftover flat greys
- Added an indigo→violet→teal accent gradient for the app mark, the active tab
  indicator (with accent glow) and the empty-state icon tile
- Regrouped the toolbar into segmented pills; rebuilt the status bar as a chip system
  covering view mode, caret position, unsaved state, zoom and feature flags
- Native rounded window corners on Windows 11 (no-op on Windows 10)
- Editor typography: 26px line height, roomier padding, accent caret, themed selection
  and overlay-style scrollbars
- Audited every surface/text pairing in both themes for WCAG AA contrast

*Tabs*
- Fixed tabs being visually clipped at the bottom of the tab strip: the strip had
  asymmetric padding around a tab taller than its container
- Added a right-click tab menu (Close Others / to the Right / All, Copy File Path,
  Show in File Explorer), double-click-to-new-tab, and file-path tooltips

*Bionic Reading*
- Fixed bionic text rendering invisible in dark mode. The processor copied
  `run.Foreground` onto generated runs; on a detached `FlowDocument` that resolves to
  the framework default (black) and, stamped as a local value, permanently defeated
  theme inheritance
- Bionic Reading no longer forces the editor read-only. Bionic runs now carry a marker
  holding the author's real font weight, which the Markdown serializer honours, so the
  document round-trips cleanly and both split panes stay writable

*Fixes*
- Restored the "Start with a thought" empty state, which was painting behind two
  opaque editor backgrounds and refreshed on only some edit paths
- Fixed the Settings font summary never updating: it lived inside a `HeaderTemplate`
  and was looked up via `Template.FindName`, which searches the control template and
  always returned null
- Fixed tooltips rendering as blank dark bars in light theme — the global `TextBlock`
  style overrode the tooltip's inherited foreground
- Fixed the tab file-path tooltip surfacing over the editor surface: a tooltip set on a
  `TabItem` is reachable from its `Content` through the logical tree
- Changing a font setting no longer silently discards the active zoom level
- Default editor font is now Times New Roman 14pt (existing settings are untouched)

*Infrastructure*
- Zoom shortcuts (Ctrl+Plus / Ctrl+Minus / Ctrl+0) are now actually bound — the status
  bar had advertised them since 1.2 but only the buttons ever worked
- Added a regression test asserting that every shortcut advertised in an
  `InputGestureText`, a tooltip or the README table has a matching `KeyBinding`, so an
  unbacked promise fails the build instead of being found by hovering
- Added a tag-driven release workflow publishing a self-contained executable and SHA256

### Version 1.6.0 (August 2026)
**Reliability, Navigation & 2026 Visual Refresh**
- Prevented view and split-mode changes from silently saving unsaved work
- Rebuilt Split View as two independently selected documents instead of synchronized mirrors
- Fixed split-pane event leaks and restored live status updates in both panes
- Preserved the original filename when Save As fails
- Restored the correct active file when unsaved tabs are omitted from a session
- Added a complete Go to line experience with Ctrl+G
- Added drag-and-drop opening and first-class `.md` / `.markdown` file filters
- Refreshed the light and dark palettes, typography, tabs, toolbar, editor, empty state, settings, and dialogs
- Removed unused Pro gating and converter scaffolding
- Expanded regression coverage for find and replace

### Version 1.5.1 (August 2026)
**Link Detection Hardening & Automated Tests**
- Prevented email addresses from producing overlapping domain links
- Improved URL normalization and trailing punctuation handling
- Prevented duplicate or stale links during editor redraws
- Applied auto-link setting changes immediately in normal and split editor views
- Corrected hyperlink hit-testing to use exclusive end offsets
- Added 13 permanent xUnit regression tests for link parsing
- Added Windows GitHub Actions checks for builds and tests

### Version 1.5 (February 2026)
**View Modes, Tab Navigation & Drag-Drop Reorder**
- Full Screen mode (F11): maximized, no chrome, tab strip visible, exit button
- Post-It mode (F12): same window bounds, only editor visible
- Distraction Free mode (Ctrl+Shift+F): full screen with centered ~900px reading width
- Always On Top toggle (View menu, persists across restarts)
- Esc exits special modes safely (respects open dialogs/popups/context menus)
- Ctrl+Tab / Ctrl+Shift+Tab: cycle through tabs (wraps)
- Ctrl+1..9: jump to tab by number (Ctrl+9 = last tab)
- Ctrl+Shift+Left/Right: move current tab left/right in strip
- Drag-and-drop tab reorder
- ViewModeManager MVVM service with RelayCommand infrastructure

### Version 1.4 (February 2026)
**Email Auto-linking, Bold/Italic Fixes & Self-Contained Publish**
- Email addresses now auto-detected and linked as `mailto:` in both Formatted and Source views
- Fixed empty bold/italic markers (`****` / `**`) appearing after deleting formatted text
- Bold/italic formatting now preserves text selection instead of losing it
- Document re-parse (link detection) now preserves selection, not just caret position
- Self-contained single-file publish: no .NET runtime required on target machine
- Dictionary files properly included in publish output

### Version 1.3 (February 2026)
**Notepad-style Tabs, Dialog Fixes & Bug Squashing**
- Redesigned tab strip with Notepad-like rounded top corners and inline "+" new tab button
- Horizontal scroll overflow for many open tabs
- Ctrl+T new tab, Ctrl+W close tab, middle-click close tab
- Unsaved indicator dot swaps with close X on hover (Notepad behavior)
- Fixed Insert Link and Confirm Link dialogs: buttons no longer clipped off-screen (auto-sizing height)
- Both dialogs now properly support dark mode with theme-aware styling
- Fixed Find/Replace dialog not updating editor reference when switching tabs
- Fixed memory leak: MainWindow event handlers properly unsubscribed on tab close
- Prevented duplicate close-button wiring on tab Loaded events
- Unified title bar background with header surface
- Adjusted light theme hover/pressed colors for better contrast

### Version 1.2 (February 2026)
**Modern Windows 11 UI Overhaul**
- Custom title bar with theme-matching caption buttons and native window behavior
- Unified header shell combining title bar, menu, and toolbar into cohesive surface
- Modern toolbar with consistent icon styling, hover states, and overflow support
- Redesigned tabs with clean underline indicator and visual unsaved dots
- Modernized status bar with clickable controls, word count, and save state indicator
- Comfortable editor padding and empty state placeholder
- Polished menu dropdowns with proper shadows and rounded corners
- Fixed Find/Replace dialog editor reference on tab switch
- Fixed hyperlink insertion position bug
- Fixed right-click selection boundary check
- Fixed memory leak in EditorControl event handling
- Fixed spell check suggestion closure bug
- Theme consistency improvements across all UI elements

### Version 1.1 (February 2026)
**Release Hardening**
- Crash-safe saves with atomic write operations
- Memory leak fixes and proper event cleanup
- Dark mode theming polish
- Zero compiler warnings
- Settings propagation improvements

### Version 1.0 (February 2026)
**Initial Release**
- Full markdown editing with Formatted and Source views
- Smart spellcheck (English, skips URLs/domains)
- Bionic Reading mode with three strength levels
- Find/Replace with industry-standard implementation
- Safe link handling with confirmation dialogs
- Session management (restore tabs, recent files, reopen closed)
- Modern Windows 11 UI with Light/Dark theming
- Two-document split workspace for side-by-side reference and editing
- Comprehensive settings window
- Context menu support throughout

## Screenshots

*(Coming soon)*
