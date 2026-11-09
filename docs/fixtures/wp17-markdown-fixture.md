# VariShot build plan (WP-17 synthetic fixture)

This fixture is **synthetic** — no private documents. It exercises every
construct WP-17 renders. Open it in *Formatted* view, then switch to Source
and confirm nothing changed. See the [AIPF control record](../AIPF_CONTROL_RECORD.md).

## Recommendation

Ship the capture pipeline first, with `AVCaptureSession` behind a protocol so
tests can fake it. Use ***bold-italic*** sparingly. Contact: dev@example.com,
docs at https://example.com/varishot, defanged: hXXps://evil[.]example/x and evil[.]example.

### Phases

1. Capture
   1. Session setup
   2. Permissions prompt
2. Processing
   - Frame buffer pool
   - Metal shaders
     - Tone mapping
3. Export

- Unordered top
* Star marker item
+ Plus marker item

#### Component matrix

| Component | Choice | Notes |
|:----------|:------:|------:|
| Capture | AVFoundation | Long technical description that should wrap inside the cell when the window is narrow, without losing any text at all |
| Storage | **SQLite** via `GRDB` | Pipe inside a cell: a \| b |
| Network | URLSession | Retries with exponential backoff; see https://example.com/retry |

##### Code

```swift
import AVFoundation

final class CaptureController {
    // Markdown inside code stays literal: **not bold**, [not](a link), | a | b |
    let url = URL(string: "https://example.com/upload")!
	func start() { /* tab-indented */ }
}
```

~~~xml
<epp xmlns="urn:ietf:params:xml:ns:epp-1.0">
  <command><info><domain:info xmlns:domain="urn:ietf:params:xml:ns:domain-1.0"/></info></command>
</epp>
~~~

###### Notes

> Measure twice, ship once.
> > A nested quote with *emphasis*.

---

Unicode check: café, naïve, Straße, 日本語, emoji ☃.

<div>Raw HTML is shown literally and never executed.</div>

Unclosed fence below stays literal text:

```
this fence is never closed
