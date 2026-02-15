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
- Non-destructive (view effect only)
- Intelligently skips URLs and technical tokens

### 🎨 Modern UI & Polish
- **Custom Windows 11 Title Bar**: Native-looking caption buttons that adapt to themes
- **Unified Header Shell**: Title bar, menu, and toolbar feel like one cohesive surface
- **Modern Toolbar**: Standardized icons with smooth hover states and overflow support
- **Notepad-style Tabs**: Rounded top corners, inline "+" new tab button, unsaved dot/close X swap on hover
- **Enhanced Status Bar**: Clickable controls, real-time line/column/word count, save state indicator
- Light/Dark/System theme modes with instant toggle
- Consistent theming across all controls (no white flashing)
- Comfortable editor padding for better reading experience
- Empty state placeholder for new documents
- Zoom controls (50% - 300%)
- Clean, distraction-free interface with reduced border noise

### 📂 Advanced Tab Management
- Multiple open documents with Notepad-style tab strip
- Inline **+** button to create new tabs (always visible after last tab)
- Horizontal scroll overflow when many tabs are open
- Visual unsaved indicator dot (swaps with close X on hover)
- Close tab confirmation if unsaved
- Middle-click to close a tab
- **Recent Files** list (File menu)
- **Reopen Closed Tab** (Ctrl+Shift+T)
- **Restore Open Tabs** on startup (optional)
- Session persistence
- Standard shortcuts: Ctrl+T (new tab), Ctrl+W (close tab), Ctrl+O, Ctrl+S

### ⚙️ Comprehensive Settings
- Windows 11 Notepad-style Settings window
- **App Theme**: Light / Dark / System
- **Text Formatting**: Font family, font size, word wrap
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
| Ctrl+Shift+M | Toggle View Mode |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+Click | Open Link |

## Download & Install

**Portable (no install needed):** Download the latest release from [GitHub Releases](https://github.com/S-Pillow/Workpad/releases). The self-contained package includes the .NET runtime — no separate installation required.

The download contains:
- `WorkNotes.exe` — the application (self-contained, ~155 MB)
- `Dictionaries/` — spell check dictionary files (must stay alongside the exe)

Just extract and run.

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
- **Polish**: Custom Windows 11 chrome, unified header design, no white flashing, consistent theming everywhere
- **Modern UX**: Clickable status bar controls, comfortable editor padding, reduced visual noise

## Release History

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
- Split view feature for side-by-side editing
- Comprehensive settings window
- Context menu support throughout

## Screenshots

*(Coming soon)*
