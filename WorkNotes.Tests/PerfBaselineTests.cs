using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows.Media;
using WorkNotes.Services;

namespace WorkNotes.Tests;

/// <summary>Throwaway: WP-17 pre-change baseline at 5fe80ed. Same generator and
/// harness as MarkdownStructuredTests.Performance_*. Not for merge.</summary>
public class PerfBaselineTests
{
    [Fact]
    public void Baseline_ParseAndSerialize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var report = new StringBuilder("WP-17 BASELINE perf @5fe80ed (ms):");
                foreach (var targetBytes in new[] { 10_000, 500_000, 3_000_000 })
                {
                    var text = BuildDocument(targetBytes);
                    var parser = new MarkdownParser(Brushes.DodgerBlue);
                    parser.ParseToFlowDocument(text.Substring(0, Math.Min(text.Length, 2000)));
                    var sw = Stopwatch.StartNew();
                    var doc = parser.ParseToFlowDocument(text);
                    var parseMs = sw.ElapsedMilliseconds;
                    sw.Restart();
                    new MarkdownSerializer().SerializeToMarkdown(doc);
                    var serializeMs = sw.ElapsedMilliseconds;
                    report.Append($" | {text.Length / 1000}KB parse={parseMs} ser={serializeMs}");
                }
                TestContext.Current.SendDiagnosticMessage(report.ToString());
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string BuildDocument(int targetBytes)
    {
        var sb = new StringBuilder();
        var section = 0;
        while (sb.Length < targetBytes)
        {
            section++;
            sb.Append($"## Section {section}\r\n\r\n");
            sb.Append("Paragraph with **bold**, *italic*, `code`, https://example.com/path and defanged evil[.]example text.\r\n\r\n");
            sb.Append("- item one\r\n  - nested item\r\n- item two\r\n\r\n");
            sb.Append("| Key | Value | Notes |\r\n|---|:-:|--:|\r\n| a | 1 | long note text here |\r\n| b | 2 | more \\| notes |\r\n\r\n");
            sb.Append("```xml\r\n<epp><command><info/></command></epp>\r\n```\r\n\r\n> quoted line\r\n\r\n");
        }
        return sb.ToString();
    }
}
