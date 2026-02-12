using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WorkNotes.Controls
{
    /// <summary>
    /// Wrapper for EditorControl that provides focus tracking and active border visual.
    /// Used in split view to make it clear which pane is receiving commands.
    /// </summary>
    public partial class EditorPane : UserControl
    {
        private EditorControl? _editorControl;

        public event EventHandler<RoutedEventArgs>? GotPaneFocus;
        public event EventHandler<RoutedEventArgs>? LostPaneFocus;

        public EditorPane()
        {
            InitializeComponent();

            // Track focus changes in the entire visual tree
            this.GotKeyboardFocus += EditorPane_GotKeyboardFocus;
            this.LostKeyboardFocus += EditorPane_LostKeyboardFocus;
            this.IsKeyboardFocusWithinChanged += EditorPane_IsKeyboardFocusWithinChanged;
        }

        /// <summary>
        /// Gets or sets the wrapped EditorControl.
        /// </summary>
        public EditorControl? EditorControl
        {
            get => _editorControl;
            set
            {
                if (_editorControl != null)
                {
                    EditorHost.Children.Remove(_editorControl);
                }

                _editorControl = value;

                if (_editorControl != null)
                {
                    EditorHost.Children.Add(_editorControl);
                }
            }
        }

        /// <summary>
        /// Gets or sets the pane ID for debugging/logging.
        /// </summary>
        public string PaneId { get; set; } = "Unknown";

        /// <summary>
        /// Gets whether this pane is currently active (has focus).
        /// </summary>
        public bool IsActive { get; private set; }

        private void EditorPane_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateActiveState(true);
        }

        private void EditorPane_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Only deactivate if focus moved outside this pane
            if (!this.IsKeyboardFocusWithin)
            {
                UpdateActiveState(false);
            }
        }

        private void EditorPane_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            bool hasFocus = this.IsKeyboardFocusWithin;
            UpdateActiveState(hasFocus);
        }

        private void UpdateActiveState(bool isActive)
        {
            if (IsActive != isActive)
            {
                IsActive = isActive;

                // Update visual border
                var accentBrush = TryFindResource("App.Accent") as SolidColorBrush ?? Brushes.DodgerBlue;
                ActiveBorder.BorderBrush = isActive ? accentBrush : Brushes.Transparent;

                // Notify listeners
                if (isActive)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditorPane] Pane '{PaneId}' became ACTIVE");
                    GotPaneFocus?.Invoke(this, new RoutedEventArgs());
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[EditorPane] Pane '{PaneId}' became INACTIVE");
                    LostPaneFocus?.Invoke(this, new RoutedEventArgs());
                }
            }
        }

        /// <summary>
        /// Sets focus to the wrapped editor control.
        /// </summary>
        public void FocusEditor()
        {
            _editorControl?.Focus();
        }
    }
}
