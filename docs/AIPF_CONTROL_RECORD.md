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
- Status: IN PROGRESS — first pass complete on Windows session (devin-68797bcdedf04bd79875b48d0e86cc30), packaged app driven end-to-end @ c5f300f: real taskkill /F kills, real external edits (incl. atomic temp+rename), on-disk recovery-dir inspection, annotated recording.
- WP-02 PASS: dirty untitled restore after kill; split bottom-buffer restore; Sanitize→exit→restart restores transformed text; Don't-Save leaves zero bytes + no resurrection. One FAIL found and fixed (6e1f560): dirty file-backed tab + external edit while closed now raises the conflict banner on restore (mtime compare) — retest dispatched.
- WP-03 PASS: live external modify → conflict banner; Save-As → old-path edit → no banner; atomic replace detected as change not delete; external delete → defined behavior; same file in both panes → one banner.
- WP-01 perf sanity PASS on 1.7MB note (typing responsive; word count ~2s debounce; save/view-switch flush correct). Formal p50/p95/p99 budget for DEC-01 still pending owner approval of measured numbers.
- Feature smoke PASS: tab color persists, 3 theme packs render, XML Tree (jump-to-source, cancel, truncation cap), Reopen Closed Tab incl. split, Resume Last Edit. EPP Inspector FAIL found and fixed (6e1f560): parses real <trID> element — retest dispatched.
- Caveats recorded: 28MB file open blocks UI ~35s (no async/cancel — known limitation for WP-01 scope discussion); "Unsaved" chip may linger after save; Ctrl+T occasionally swallowed with split-pane focus; large XML tree materialization ~10s freeze + render artifact at 90k nodes; same-file-both-panes leaves clean pane stale until restart.
- Screenshots captured on the Windows box for WP-16 (main tabs, XML tree, EPP inspector, themes, conflict/deleted banners).

## Assumption labels (phase 2)
- DEC-01 latency budget: measured-then-approved — pending Windows numbers, owner approval required. — GATED.
- DEC-02 PSL private-section = include all rules. — ADOPTED (proposed default).
- DEC-03 theme names Ocean/Forest/WarmNeutral. — ADOPTED (proposed default).
- DEC-05 context resume = Resume Last Edit command only. — ADOPTED (proposed default).
- DEC-07 reopen existing file → focus existing tab, no overwrite. — ADOPTED (proposed default).
- XML tree "jump to source" offered only in Source view (Formatted-view node↔glyph mapping not reliable). — ASSUMED, honest-disable per WP-10-AC02.

**PAU-6 Post-review hardening round** — external review list + Devin Review findings, commits f8d9970..0b26b86.
- Fix-before-merge items: pane-cancel freeze/crash closed (busy overlay closes on cancel, cancel callback cleared + disposal-safe, app-level DispatcherUnhandledException/AppDomain handlers flush a best-effort snapshot before death); ReopenClosedTab data-loss paths closed (missing top file no longer discards bottom state, bottom-only restore marks dirty, reopen-onto-existing-tab no longer stomps the live bottom pane, empty pane not marked dirty); IDN rules loaded in punycode space (Unicode PSL rules now match; regression test added); FlushFormattedSync inserted before the four stale canonical-text reads; RecordLastEdit gated on EditorControl.IsProgrammaticUpdate.
- Improvements taken: XML tree skips whitespace-only text nodes and enforces the node cap on attributes; EPP Inspector reports the object name and recognizes real extension namespaces (secDNS/rgp/launch/fee-*/etc.); compare LCS bound is by matrix cells and the work cap raised to 2M.
- Hygiene: .gitattributes added (CRLF convention preserved verbatim); ~3,400 phantom diff lines removed; 36MB dead dictionary files deleted.
- Devin Review rounds on this branch: 12 findings resolved, 1 rejected (dismissed conflict banner must stay dismissed — contradicts the prior round's own finding).

## PAU-7 PR #9 targeted acceptance verification (2026-11-09) — request issuecomment-6732585403

**Build under test:** branch `devin/workpad-phases` @ `fb4f0d5` (baseline `5fe80ed` + same-file fix below). Packaged EXE = CI artifact `WorkNotes-win-x64` from run 42569532220 (pull_request merge ref `da5ce8b` = `fb4f0d5` merged onto main `1178132`), ProductVersion `1.7.6+da5ce8b`, EXE SHA-256 `727B09C3E1D90A0EBE217C84497FDDB8DE751CB8DFA0A5B6B77C758FCB415CF0`.
**Observer / evidence source:** Claude (Opus) via Claude Desktop computer use + local PowerShell on the owner's Windows 11 PC (borg-cube, 2560x1440), packaged EXE run from a disposable folder (`%USERPROFILE%\WorkpadQA-20261109`). Owner's real `%LocalAppData%\WorkNotes` was renamed aside before testing and renamed back after (2,282 bytes, timestamps unchanged); the throwaway test profile was left as `WorkNotes.qa-testprofile-20261109` for the owner to delete. Screenshots: `docs/evidence/pr9-acceptance/`.

**Reused evidence (not repeated):** prior PAU-5 WP-02/WP-03/feature-smoke passes; Windows CI. Re-run only because code changed: CI windows-latest @ fb4f0d5 — build OK, **168/168 tests pass** (164 prior + 4 new), packaged-exe smoke PASS.

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Recovery after external change (fix 6e1f560): dirty file-backed tab, snapshot taken, process force-killed, file modified on disk while closed, relaunch | **PASS** — buffer restored with the unsaved edit (not overwritten); conflict banner "recovery-conflict.md changed on disk while you have unsaved edits"; disk untouched by restore; Compare shows correct disk-vs-tab diff; Keep My Version dismisses banner and leaves disk untouched; explicit Save then writes the kept buffer | screenshots 07–09; PowerShell disk reads before/after each step |
| 2 | EPP Inspector trID (fix 6e1f560), realistic domain:info response with secDNS-1.1 extension and `<trID><clTRID/><svTRID/></trID>` | **PASS** — clTRID `QA-CLIENT-TRID-0001`, svTRID `QA-SERVER-TRID-54321`, result 1000, name `example-qa.com`, extension secDNS-1.1 shown | screenshots 01–02 |
| 3 | Same-file split panes | **DEFECT CONFIRMED + FIXED (fb4f0d5) + PASS on Windows** — see below | screenshots 03–06; disk reads |
| 4 | WP-01 typing latency p50/p95/p99 | **NO MEASUREMENTS EXIST** — see below | repo/PR search |

**Check 3 finding (data-loss, safety-critical):** the same file opened in both split panes is two independent `Document` copies. External-change handling acted only on the first matching copy (`FindTabForPath`), and our own save is suppressed as a watcher echo. Demonstrated paths before the fix: (a) save in one pane → other pane stays stale and "clean" → editing and saving it silently overwrites the newer file; (b) external change with clean top copy and dirty bottom copy → top quietly reloads and the dirty bottom copy gets **no** conflict banner, so its later save overwrites the external edit. **Fix:** `SameFileCopies.Plan` decides per open copy (saving copy → none, clean → reload its own pane, dirty → conflict banner); `ApplyDiskChangeToCopies` applies it on external change/delete and after single-editor and split-pane saves. 4 regression tests (SameFileCopiesTests). **Windows retest PASS:** A — save in right pane → clean left pane refreshed ("saved from another pane"), disk correct; B — left pane dirty, right pane saves → left keeps its unsaved text + banner "was saved from another pane while this copy has unsaved edits", disk = right's save; Reload from Disk then syncs left; C — left clean, right dirty, external PowerShell write → left reloads, right keeps unsaved text + conflict banner (the pre-fix silent path).

**Check 4 (WP-01) — measured vs assumed:**
- Measured: none. No p50/p95/p99 keystroke latency exists in this record, the PR thread, CI, or the code (recovery-service Stopwatch instrumentation measures snapshot capture, not typing). Only qualitative evidence: PAU-5 "typing responsive on a 1.7 MB note"; this session's GUI typing on small files had no observable lag (not a measurement).
- Proposed budget (assumption, for owner approval under DEC-01): keystroke → rendered frame in Formatted view p95 ≤ 16 ms, p99 ≤ 33 ms for notes ≤ 1 MB; debounced idle flush (400 ms) ≤ 100 ms p95 at 1 MB; files > 10 MB and the 28 MB open freeze excluded (separate limitation). Method: instrument PreviewTextInput → next CompositionTarget.Rendering timestamp in a diagnostic build, 500 keystrokes per size (10 KB / 500 KB / 1.7 MB), report p50/p95/p99 + machine spec.
- Recommendation: do not block PR #9 on DEC-01 if the owner accepts the qualitative evidence and schedules the instrumented measurement before the combined release; otherwise measure first.

**New minor observation (non-blocking):** after crash-restore, an empty Untitled tab is restored marked "Unsaved" (empty buffer; only consequence is an unnecessary save prompt).

**Disposition of existing caveats:** same-file-both-panes stale pane — RESOLVED (fb4f0d5). Still open, non-blocking, documented: 28 MB open blocks UI ~35 s; Ctrl+T swallowed with split-pane focus (reproduced this session); large XML tree ~10 s freeze at 90k nodes; empty-Untitled-restored-dirty (new). "Unsaved chip lingers after save" — not reproduced this session (chip cleared on save).

**Conclusion: READY TO MERGE** — all functional acceptance checks pass on the packaged Windows build, the one safety-critical defect found is fixed with regression coverage and Windows retest, CI 168/168 green. Owner decision still required at merge: DEC-01 (accept proposed WP-01 budget/measurement plan or require measurement first). Not done (out of scope / not authorized): merge, PR #10 changes, tag, release.
