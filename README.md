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
- Windows 11 Fluent design with proper two-tone dark theme
- Light/Dark/System theme modes
- Consistent theming across all controls (no white flashing)
- Enhanced status bar with line/column indicator
- Zoom controls (50% - 300%)
- Feature indicators (📝 Spellcheck, ⚡ Bionic, ⬌ Split View)
- Clean, distraction-free interface
- Organized toolbar with visual grouping

### 📂 Advanced Tab Management
- Multiple open documents with clean tab styling
- Dirty tracking with visual indicator (•)
- Close tab confirmation if unsaved
- **Recent Files** list (File menu)
- **Reopen Closed Tab** (Ctrl+Shift+T)
- **Restore Open Tabs** on startup (optional)
- Session persistence
- Standard shortcuts: Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+W

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
| Ctrl+N | New Tab |
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

## Requirements

- Windows 10/11
- .NET 8.0 Runtime

## Building

```bash
dotnet build WorkNotes.sln
```

## Running

```bash
dotnet run --project WorkNotes
```

Or run the compiled executable from `bin/Debug/net8.0-windows/WorkNotes.exe`

## License

This project is open source and available under the MIT License.

## Why Work Notes?

Built specifically for users who work with lots of URLs, domains, and technical content in their notes. Features like intelligent spellcheck (that skips domains), smart link handling with safety confirmations, bionic reading mode, and dual-view editing make it perfect for developers, sysadmins, and technical writers.

### Key Differentiators
- **Domain/URL-Aware**: Spellcheck and bionic reading skip technical tokens
- **Safety First**: Link confirmation dialogs prevent accidental navigation
- **Dual Representation**: True formatted view with markdown storage (not just syntax highlighting)
- **Session Management**: Remembers your open tabs and recently closed files
- **Productivity Focus**: Zoom, line/column tracking, comprehensive keyboard shortcuts
- **Polish**: Two-tone dark theme, no white flashing, consistent theming everywhere

## Release History

### Version 1.0 (February 2026)
**Initial Release**
- Full markdown editing with Formatted and Source views
- Smart spellcheck (English, skips URLs/domains)
- Bionic Reading mode with three strength levels
- Find/Replace with industry-standard implementation
- Safe link handling with confirmation dialogs
- Session management (restore tabs, recent files, reopen closed)
- Modern Windows 11 UI with perfect Light/Dark theming
- Enhanced status bar with zoom and indicators
- Comprehensive settings window
- Context menu support throughout

## Screenshots

*(Coming soon)*
