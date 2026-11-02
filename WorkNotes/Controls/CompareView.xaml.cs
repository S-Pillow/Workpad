using System.Windows.Controls;
using WorkNotes.Services;

namespace WorkNotes.Controls
{
    /// <summary>Renders a CompareResult as a line diff (removed/added tinted).</summary>
    public partial class CompareView : UserControl
    {
        public CompareView()
        {
            InitializeComponent();
        }

        public void ShowResult(CompareResult result, string leftLabel, string rightLabel)
        {
            DiffList.Items.Clear();
            DiffList.Items.Add(new DiffLine { Kind = DiffLineKind.Same, Text = $"── {leftLabel} ── vs ── {rightLabel} ──" });
            if (result.Identical)
            {
                DiffList.Items.Add(new DiffLine { Kind = DiffLineKind.Same, Text = "(identical)" });
                return;
            }
            foreach (var line in result.Lines)
            {
                var prefix = line.Kind == DiffLineKind.Removed ? "- " : line.Kind == DiffLineKind.Added ? "+ " : "  ";
                DiffList.Items.Add(new DiffLine { Kind = line.Kind, Text = prefix + line.Text });
            }
        }
    }
}
