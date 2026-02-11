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

### 🔍 Find & Replace
- Powerful find/replace with Ctrl+F / Ctrl+H
- Match case and whole word options
- Wrap-around search
- Replace All with single undo step
- Works perfectly in both Formatted and Source views

### 🔗 Smart Link Handling
- Clickable URLs, domains, and emails in Formatted view
- Ctrl+Click to open links
- Safety confirmation dialog before opening
- Right-click context menu: "Open Link" / "Copy Link Address"
- Toggle auto-link detection in Settings

### ✍️ Spellcheck
- English dictionary with red underline for misspelled words
- Right-click for suggestions and corrections
- Custom user dictionary (Add to Dictionary)
- Intelligently skips URLs, domains, and emails
- Toggle on/off in Settings

### 👁️ Bionic Reading Mode
- Optional reading enhancement that bolds the first letters of words
- Three strength levels: Light, Medium, Strong
- Toggle on/off in View menu or Settings
- Non-destructive (view effect only)

### 🎨 Modern UI
- Windows 11 Fluent design
- Light/Dark/System theme modes
- Theme-aware colors throughout
- Clean, distraction-free interface

### 📂 Tab Management
- Multiple open documents
- Dirty tracking with visual indicator (•)
- Close tab confirmation if unsaved
- Standard shortcuts: Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+W

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
| Ctrl+B | Bold |
| Ctrl+I | Italic |
| Ctrl+K | Insert Link |
| Ctrl+F | Find |
| Ctrl+H | Replace |
| Ctrl+Shift+M | Toggle View Mode |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |

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

Built specifically for users who work with lots of URLs, domains, and technical content in their notes. Features like intelligent spellcheck (that skips domains), smart link handling, and dual-view editing make it perfect for developers, sysadmins, and technical writers.
