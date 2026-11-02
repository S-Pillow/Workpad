# Workpad 2.x — AIPF Project Control Record

Framework: Neutral AIPF v8.10.1. Governing product doc: `Workpad_2x_Product_Requirements_Document.docx` (owner-supplied PRD, Oct 29 2026).

## Routing (Section 8, deterministic)
- Order 1 baseline: **Chapter 12** — existing application (`S-Pillow/Workpad`, WPF/.NET).
- Order 2 form: **Chapter 16** — standalone packaged desktop executable (self-contained win-x64).
- Order 3 behavior: **Chapter 14** — UI/operator readiness; shell redesign = "Visual direction / substantial redesign" under the approved PRD (not silently changing workflow semantics). **Chapter 15** light activation: XML/URL validation produces user-facing findings (honest error reporting, no invented confidence).
- Order 4 execution: **Chapter 19** — remote CI build/test on GitHub `windows-latest` runners.
- Order 5 release: **Chapter 20** — PR merge + tagged release are release transitions; evidence ledger below.
- Chapter 21: N/A — single-agent direct execution (derivation partition/transport/handoff = N/A).
- Chapter 17: update delivery decided = **manual "Check for Updates"** against GitHub Releases (read-only compare + link). No auto-download, no installer, no self-replacement.

## Treatment: Managed
Section 9.2 fails: new persistence (recovery snapshots), new release boundary changes, operator-readiness evidence not fully deterministic on this build host. Full preflight + chapter artifacts apply.

## Phase -2 existing-system baseline
- Project root: `~/repos/Workpad` → github.com/S-Pillow/Workpad, main @ 4fd40f0. Work branch: `devin/workpad-2x`.
- Stack: WPF, .NET 8 (`net8.0-windows`), AvalonEdit source editor + RichTextBox formatted editor; xUnit tests; GitHub Actions build/test on windows-latest; tag-driven single-file self-contained release.
- Existing relevant behavior: tab strip + context menu + reopen-closed-tab; session.json restores **file paths only** (untitled/dirty content lost); split view = two-doc side-by-side; link detection + confirm dialog; spellcheck; bionic; themes; settings.json in %LocalAppData%\WorkNotes.
- Durability: FileHelper.AtomicWriteText (temp+rename) — reuse for recovery snapshots.
- Push remote: confirmed (Devin git proxy). Build host: Linux VM — compile check only via `EnableWindowsTargeting=true`; WPF runtime/tests require Windows (CI or Windows session).

## Risk & Decision Register (T2)
| Finding | Severity | Disposition | Note |
|---|---|---|---|
| WPF cannot run on Linux VM; UI/operator evidence not observable here | Medium | Accepted — mitigation | CI (windows-latest) runs build+tests on PR; packaged-exe smoke added to workflows; rendered/operator evidence marked pending for Windows observation |
| `Get Domains` needs real eTLD+1; Nager.PublicSuffix has no offline list (violates local-first) | Medium | Resolved | Self-contained `RegistrableDomain` resolver with curated embedded public-suffix set; documented coverage; no network |
| Unsaved-content restore could surprise users expecting prompts | Low | Resolved — PRD | PRD P-04/TAB-02 explicitly approves silent session persistence; explicit dirty-tab close still prompts |
| Recovery snapshot retention/disk growth | Low | Resolved | Conservative retention, orphan cleanup, Settings "Clear session/recovery data" |

## Authorization units
**AU-1 Foundation** — .NET 10 migration; session v2 persisting untitled+dirty tabs via SessionRecoveryService; tab locking; shell toolbar removal (opt-in restore); Tools menu; ExternalFileMonitor + conflict banner; Close All/Start Fresh.
- Allowed: csproj, workflows, Models/, Services/, MainWindow, AppSettings, SettingsWindow, docs.
- Forbidden: public plugin SDK, workspace mode, XML→JSON, AI features, auto-update machinery.
- Acceptance: TAB-01..06, LOCK-01..06, RECOV-01..06, AC-01..04, shell has no permanent toolbar; conflict banner per §13.2; compile clean.
- Stop: after commit + compile check; merge/release not authorized in-unit.

**AU-2 Transform pipeline + tools** — TextTransformService contract (selection-first, validate→compute→replace, one undo); TechnicalIndicatorService; URL tools (Sanitize/Unsanitize/Get Domains/Get Hosts); XML tools (Beautify/Minify/Validate, DTD/entity disabled, line-col errors); CommandRegistry + Ctrl+Shift+P palette; menu/context wiring.
- Acceptance: URL-01..08, XML-01..06, REC-06, §16.1 contract, AC-06..10; adversarial inputs covered by tests.

**AU-3 Compare + polish** — CompareService line diff; secondary-pane host; Compare with Saved / Another Tab / XML-normalized; source line numbers; separate source font; Settings additions; Help→Check for Updates (Ch17 manual model); accessibility (lock glyph non-color, automation names); README parity (Ch13).
- Acceptance: CMP-01..06, AC-11, P1 font/line-number items, README truthful.

**AU-4 Tests + PR** — unit tests (incl. adversarial: XXE/DTD, already-sanitized, co.uk, malformed XML, empty/binary-ish input); compile; PR open with evidence map; Windows CI green = gate for merge (merge/release NOT self-authorized).

## Evidence ledger (Ch 20, claims → observer)
- Compile clean (net10): `dotnet build WorkNotes.sln` 0 warnings/0 errors — Devin/Linux (source-level only). Verified after each AU commit.
- Unit tests: written (52 cases incl. adversarial); `dotnet test` runs on windows-latest CI — remote (Ch19), raw status in PR checks. Not run locally: net*-windows TFM requires Windows.
- Packaged exe smoke: workflow publish + launch check — remote runner.
- UI rendered/operator evidence (shell, palette, banners, lock glyph): **pending** — requires Windows operator observation; not claimed.

## Unit status
- AU-1 Foundation: COMPLETE (b9d55e4, aec2c53 — pushed)
- AU-2 Transform pipeline + tools: COMPLETE (aec2c53 — pushed)
- AU-3 Compare + polish: COMPLETE (2a8f522 — pushed; pending final CI)
- AU-4 Tests + PR: IN PROGRESS (bee7632 tests pushed; PR pending)

## Assumption labels (owner may override)
- "Established safe/defanged representation" implemented as `hXXp(s)://host[.]tld` (scheme + host dots defanged, path preserved); Unsanitize accepts both `hXXps://x[.]y` and `hXXps[://]x[.]y` forms per URL-03. — ASSUMED, flag if reference app differs.
- Public-suffix coverage = curated common set (co.uk, com.au, etc.); exotic suffixes document as limitation. — ASSUMED.
- "Close All / Start Fresh" skips locked tabs per LOCK-03 precedence. — ASSUMED.
