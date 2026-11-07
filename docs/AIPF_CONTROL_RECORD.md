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
| `Get Domains` needs real eTLD+1; Nager.PublicSuffix has no offline list (violates local-first) | Medium | Accepted temporary limitation — resolve before 2.0 release | Self-contained `RegistrableDomain` resolver with curated embedded public-suffix set; exotic suffixes fall back to last-two-labels; must bundle a pinned full Public Suffix List locally (still no runtime network) |
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
- Unit tests: 96 cases incl. adversarial — PASSED 96/96 on windows-latest CI at PR head, plus win-x64 publish + packaged-EXE launch smoke.
- Packaged exe smoke: workflow publish + launch check — remote runner.
- UI rendered/operator evidence (shell, palette, banners, lock glyph): **pending** — requires Windows operator observation; not claimed. Packaged-exe smoke step present in workflow.
- Recovery perf: no synchronous UI-thread disk I/O (writer thread); unchanged buffers emit no capture/write (version gating + hash reuse); forced-crash consistency covered at service level (manifest-orphan + manifest-deleted tests) — full process-kill and UI-thread latency measurement under load **pending** Windows observation; formatted-editor sync audit measurement-only, not yet executed.
- Recovery behavioral matrix rows needing a running app (external-edit conflict prompts, Don't-Save-twice cleanup, sanitize→restart, killed-process vs clean exit): **pending** Windows operator observation; service-level and monitor-level facts covered by unit tests.

## Unit status
- AU-6 Recovery performance redesign: COMPLETE (3c00237→head — approved-order implementation: instrumentation, revision/version gating, AvalonEdit immutable snapshots, one latest-wins background writer, immutable revisioned content files + atomic manifest-last commit, 2s idle / 30s ceiling scheduling, deterministic shutdown flush; crash-consistency, surrogate-boundary, dedup, 10×2MB perf smoke tests; monitor behind IFileMonitor with injected-event tests; Keep-unsaved greyed when Restore off; explicit "Don't Save" excluded from manifest + snapshot deleted; SecondaryPaneHost persists through split transitions; approved by user)
- AU-5 Review remediation: COMPLETE (0c7a740 — 8 Devin Review findings fixed: pane-aware conflicts, split-pane recovery, formatted-view transforms, monitor coverage, xml:space/CDATA; approved by user)
- AU-1 Foundation: COMPLETE (b9d55e4, aec2c53 — pushed)
- AU-2 Transform pipeline + tools: COMPLETE (aec2c53 — pushed)
- AU-3 Compare + polish: COMPLETE (2a8f522 — pushed; pending final CI)
- AU-4 Tests + PR: COMPLETE (PR #6 open; CI green at b65cb3c; merge NOT authorized — owner decides)

## Assumption labels (owner may override)
- "Established safe/defanged representation" implemented as `hXXp(s)://host[.]tld` (scheme + host dots defanged, path preserved); Unsanitize accepts both `hXXps://x[.]y` and `hXXps[://]x[.]y` forms per URL-03. — ASSUMED, flag if reference app differs.
- Public-suffix coverage = curated common set (co.uk, com.au, etc.); exotic suffixes document as limitation. — ASSUMED.
- "Close All / Start Fresh" skips locked tabs per LOCK-03 precedence. — ASSUMED.

## Phase 2 authorization units (PRD v1.1, branch devin/workpad-phases, baseline main @ 1178132)

**PAU-1 Performance & scale** — WP-01 formatted-editor perf, WP-04 bounded cancellable compare, WP-05 full offline PSL.
- Status: COMPLETE — commits 1a8a79d, 9ae9ac4, f7dc617. WP-01: keystroke path no longer serializes the FlowDocument (400ms idle debounce; authoritative FlushFormattedSync at save/view-switch/recovery/transform/compare); word count debounced off content change; allocation-free empty check. WP-04: patience-diff engine replaces full-matrix LCS (Stack worklist, unique-anchor LIS, ≤512×512 small-seg LCS, 200k-item bound → CompareTooLargeException); CompareAsync off UI thread with Cancel + stale-result guards. WP-05: pinned full PSL (16,584 lines; wildcard/exception/IDN rules; PRIVATE section included per DEC-02).
- Pending evidence: WP-01-AC01 measured p50/p95/p99 latency budget vs baseline — Windows session (DEC-01 requires owner-approved budget AFTER measurement).

**PAU-2 Experience** — WP-06 tab colors, WP-07 curated themes, WP-08 markdown round-trip, WP-09 context Tools submenu, WP-12 reopen closed tab, WP-13 context resume.
- Status: COMPLETE — commits 39edc44, f35a53c, 6c6bd15, 040cdc3, 67afbe7 (WP-09 pre-existing). WP-13 shipped as DEC-05 proposed default: persisted last-edit tab+pane + explicit "Resume Last Edit" command; no dashboards/summaries/history.

**PAU-3 Inspectors** — WP-10 XML Tree Inspector, WP-11 EPP Inspector.
- Status: COMPLETE — commit aa06f60. Shared safe parser (DTD prohibited, resolver null, ceilings); navigable tree incl. attributes/namespaces/comments/CDATA with line-info jump-to-source (Source view only, honestly disabled otherwise); EPP summary read-only with opaque unknown extensions; cancellable, stale-guarded, pane-host preserved in split view (WP-10-AC05 via AU-6 pane-host fix).

**PAU-4 Presentation** — WP-15 branding, WP-16 GitHub presentation, WP-14 seam extraction.
- Status: COMPLETE — commit b6e23f9. Public name Workpad; WorkNotes namespace/exe/LocalAppData unchanged (DEC-06 exe rename deferred). README feature/release sections added; SmartScreen + SHA-256 + release docs already present. WP-14: no further extraction justified — new logic already isolated behind testable services (CompareService, XmlTreeService, EppInspectorService) and the SecondaryPaneHost seam; coordinator stays in MainWindow per "justified only" gate.

**PAU-5 Windows operator verification** — WP-02 recovery matrix, WP-03 external-file conflicts, packaged visual evidence + WP-01 latency measurement + screenshots for WP-16.
- Status: PENDING — dispatched to Windows child session (devin-68797bcdedf04bd79875b48d0e86cc30); evidence lands in PR + this record when observed. Source-only CI cannot substitute per PRD §4.

## Assumption labels (phase 2)
- DEC-01 latency budget: measured-then-approved — pending Windows numbers, owner approval required. — GATED.
- DEC-02 PSL private-section = include all rules. — ADOPTED (proposed default).
- DEC-03 theme names Ocean/Forest/WarmNeutral. — ADOPTED (proposed default).
- DEC-05 context resume = Resume Last Edit command only. — ADOPTED (proposed default).
- DEC-07 reopen existing file → focus existing tab, no overwrite. — ADOPTED (proposed default).
- XML tree "jump to source" offered only in Source view (Formatted-view node↔glyph mapping not reliable). — ASSUMED, honest-disable per WP-10-AC02.
