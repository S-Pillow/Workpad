using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using WorkNotes.Models;
using WorkNotes.ViewModels;

namespace WorkNotes.Services
{
    /// <summary>
    /// State machine managing application view modes (Normal, FullScreen, PostIt, DistractionFree)
    /// and the Always On Top toggle.
    /// 
    /// Why Hybrid: This service owns window-chrome state and element visibility only.
    /// It never touches editor internals (EditorControl, AvalonEdit, RichTextBox, context menus,
    /// spell check, selection, or link detection). Existing code-behind for those stays untouched.
    /// 
    /// XAML binds to this service's properties for Visibility on header/statusbar/tabstrip.
    /// The Window reference is passed to ApplyViewMode() for WindowStyle/WindowState changes.
    /// </summary>
    public class ViewModeManager : INotifyPropertyChanged
    {
        // --- Saved window state for restoring from special modes ---
        private WindowState _savedWindowState = WindowState.Normal;
        private WindowStyle _savedWindowStyle = WindowStyle.SingleBorderWindow;
        private ResizeMode _savedResizeMode = ResizeMode.CanResize;
        private Rect _savedBounds = Rect.Empty;
        private bool _wasMaximized;

        // --- Current state ---
        private AppViewMode _currentMode = AppViewMode.Normal;
        private bool _isAlwaysOnTop;

        // --- Window reference (set once in MainWindow constructor) ---
        private Window? _window;

        // --- Reading-width for distraction-free mode ---
        private double _maxReadingWidth = 900;
        private Thickness _distractionFreePadding = new Thickness(0);

        public ViewModeManager()
        {
            ToggleFullScreenCommand = new RelayCommand(() => ToggleMode(AppViewMode.FullScreen));
            TogglePostItCommand = new RelayCommand(() => ToggleMode(AppViewMode.PostIt));
            ToggleDistractionFreeCommand = new RelayCommand(() => ToggleMode(AppViewMode.DistractionFree));
            ToggleAlwaysOnTopCommand = new RelayCommand(() => IsAlwaysOnTop = !IsAlwaysOnTop);
            ExitToNormalCommand = new RelayCommand(() => ApplyViewMode(AppViewMode.Normal));
        }

        /// <summary>
        /// Binds this manager to a specific window. Must be called once during MainWindow construction.
        /// </summary>
        public void Initialize(Window window)
        {
            _window = window;
            _window.SizeChanged += (s, e) => UpdateDistractionFreePadding();
        }

        #region Bindable Properties

        public AppViewMode CurrentMode
        {
            get => _currentMode;
            private set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    OnPropertyChanged();
                    // Update all computed visibility properties
                    OnPropertyChanged(nameof(IsHeaderVisible));
                    OnPropertyChanged(nameof(IsTabStripVisible));
                    OnPropertyChanged(nameof(IsStatusBarVisible));
                    OnPropertyChanged(nameof(IsExitButtonVisible));
                    OnPropertyChanged(nameof(IsDistractionFreeMode));
                    OnPropertyChanged(nameof(ExitModeLabel));
                    OnPropertyChanged(nameof(IsNormalMode));
                    UpdateDistractionFreePadding();
                }
            }
        }

        public bool IsAlwaysOnTop
        {
            get => _isAlwaysOnTop;
            set
            {
                if (_isAlwaysOnTop != value)
                {
                    _isAlwaysOnTop = value;
                    OnPropertyChanged();

                    // Apply immediately to the window
                    if (_window != null)
                        _window.Topmost = value;

                    // Persist to settings
                    App.Settings.AlwaysOnTop = value;
                    App.Settings.Save();

                    Debug.WriteLine($"[ViewModeManager] AlwaysOnTop = {value}");
                }
            }
        }

        // --- Computed visibility properties (bound by XAML) ---

        /// <summary>Title bar + menu + toolbar visible only in Normal mode.</summary>
        public bool IsHeaderVisible => _currentMode == AppViewMode.Normal;

        /// <summary>Tab strip visible in Normal and FullScreen.</summary>
        public bool IsTabStripVisible => _currentMode == AppViewMode.Normal || _currentMode == AppViewMode.FullScreen;

        /// <summary>Status bar visible only in Normal mode.</summary>
        public bool IsStatusBarVisible => _currentMode == AppViewMode.Normal;

        /// <summary>Exit button visible in all special modes.</summary>
        public bool IsExitButtonVisible => _currentMode != AppViewMode.Normal;

        /// <summary>Whether distraction-free reading margins should be applied.</summary>
        public bool IsDistractionFreeMode => _currentMode == AppViewMode.DistractionFree;

        /// <summary>Convenience for checking normal mode (e.g., for WindowChrome).</summary>
        public bool IsNormalMode => _currentMode == AppViewMode.Normal;

        /// <summary>Label for the exit overlay button.</summary>
        public string ExitModeLabel => _currentMode switch
        {
            AppViewMode.FullScreen => "Exit Full Screen",
            AppViewMode.PostIt => "Exit Post-It",
            AppViewMode.DistractionFree => "Exit Distraction Free",
            _ => ""
        };

        /// <summary>
        /// Padding applied to center content at reading width in distraction-free mode.
        /// Dynamically recalculated on window resize.
        /// </summary>
        public Thickness DistractionFreePadding
        {
            get => _distractionFreePadding;
            private set
            {
                if (_distractionFreePadding != value)
                {
                    _distractionFreePadding = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand ToggleFullScreenCommand { get; }
        public ICommand TogglePostItCommand { get; }
        public ICommand ToggleDistractionFreeCommand { get; }
        public ICommand ToggleAlwaysOnTopCommand { get; }
        public ICommand ExitToNormalCommand { get; }

        #endregion

        #region Core State Machine

        /// <summary>
        /// Toggles a mode: if already in that mode, returns to Normal. Otherwise enters it.
        /// </summary>
        private void ToggleMode(AppViewMode targetMode)
        {
            if (_currentMode == targetMode)
                ApplyViewMode(AppViewMode.Normal);
            else
                ApplyViewMode(targetMode);
        }

        /// <summary>
        /// Applies the requested view mode. This is the single entry point for all mode transitions.
        /// 
        /// Transition rules:
        /// 1. If switching from one special mode to another, return to Normal first (resets window state).
        /// 2. Save window state when leaving Normal.
        /// 3. Apply target mode's window properties.
        /// 4. Update visibility properties (XAML bindings react automatically).
        /// </summary>
        public void ApplyViewMode(AppViewMode targetMode)
        {
            if (_window == null)
            {
                Debug.WriteLine("[ViewModeManager] Cannot apply mode: window not initialized");
                return;
            }

            var previousMode = _currentMode;

            // If switching between special modes, restore Normal first, then re-save the Normal bounds
            if (previousMode != AppViewMode.Normal && targetMode != AppViewMode.Normal)
            {
                Debug.WriteLine($"[ViewModeManager] Transitioning {previousMode} -> Normal -> {targetMode}");
                RestoreNormalMode();
                // Re-save the restored Normal state so we can return to it from the new mode
                SaveWindowState();
            }
            else if (targetMode != AppViewMode.Normal && previousMode == AppViewMode.Normal)
            {
                // Save state when leaving Normal
                SaveWindowState();
            }

            // Apply the target mode
            switch (targetMode)
            {
                case AppViewMode.Normal:
                    RestoreNormalMode();
                    break;

                case AppViewMode.FullScreen:
                    ApplyFullScreen();
                    break;

                case AppViewMode.PostIt:
                    ApplyPostIt();
                    break;

                case AppViewMode.DistractionFree:
                    ApplyDistractionFree();
                    break;
            }

            CurrentMode = targetMode;
            Debug.WriteLine($"[ViewModeManager] Mode changed: {previousMode} -> {targetMode}");
        }

        /// <summary>
        /// Returns true if Esc should exit the current view mode.
        /// Call this from PreviewKeyDown; returns false if we're in Normal mode
        /// (Esc should pass through to editors/dialogs).
        /// </summary>
        public bool ShouldEscExitMode()
        {
            return _currentMode != AppViewMode.Normal;
        }

        #endregion

        #region Mode Implementations

        private void SaveWindowState()
        {
            _wasMaximized = _window!.WindowState == WindowState.Maximized;
            _savedWindowState = _window.WindowState;
            _savedWindowStyle = _window.WindowStyle;
            _savedResizeMode = _window.ResizeMode;

            // RestoreBounds gives us the Normal-state bounds even if currently maximized
            _savedBounds = _window.RestoreBounds;

            Debug.WriteLine($"[ViewModeManager] Saved state: State={_savedWindowState}, " +
                          $"Bounds={_savedBounds}, WasMaximized={_wasMaximized}");
        }

        private void RestoreNormalMode()
        {
            if (_window == null) return;

            // Restore WindowChrome caption height so the custom title bar drag area works
            SetCaptionHeight(32);

            // Restore window chrome
            _window.WindowStyle = WindowStyle.SingleBorderWindow;
            _window.ResizeMode = ResizeMode.CanResize;

            // Restore position/size: must set Normal state first to apply bounds, then re-maximize if needed
            if (_savedBounds != Rect.Empty)
            {
                _window.WindowState = WindowState.Normal;
                _window.Left = _savedBounds.Left;
                _window.Top = _savedBounds.Top;
                _window.Width = _savedBounds.Width;
                _window.Height = _savedBounds.Height;
            }

            if (_wasMaximized)
            {
                _window.WindowState = WindowState.Maximized;
            }

            Debug.WriteLine("[ViewModeManager] Restored Normal mode");
        }

        private void ApplyFullScreen()
        {
            if (_window == null) return;

            // In FullScreen, header is hidden so CaptionHeight=0 prevents a dead zone at the top
            SetCaptionHeight(0);

            // Full screen on current monitor: WindowStyle=None + Maximized
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.WindowState = WindowState.Maximized;

            Debug.WriteLine("[ViewModeManager] Applied FullScreen mode");
        }

        private void ApplyPostIt()
        {
            if (_window == null) return;

            // Post-It: hide chrome but keep same size/position
            SetCaptionHeight(0);
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.CanResize;

            // Keep current bounds (don't maximize)

            Debug.WriteLine("[ViewModeManager] Applied PostIt mode");
        }

        private void ApplyDistractionFree()
        {
            if (_window == null) return;

            // Distraction-free: fullscreen + centered reading width
            SetCaptionHeight(0);
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.WindowState = WindowState.Maximized;

            Debug.WriteLine("[ViewModeManager] Applied DistractionFree mode");
        }

        /// <summary>
        /// Adjusts the WindowChrome.CaptionHeight. In Normal mode this is 32 (for the custom title bar
        /// drag area). In special modes it's 0 so the hidden title bar area doesn't create a dead zone
        /// that intercepts mouse clicks.
        /// </summary>
        private void SetCaptionHeight(double height)
        {
            if (_window == null) return;
            var chrome = WindowChrome.GetWindowChrome(_window);
            if (chrome != null)
            {
                chrome.CaptionHeight = height;
            }
        }

        #endregion

        #region Distraction-Free Padding

        private void UpdateDistractionFreePadding()
        {
            if (_currentMode != AppViewMode.DistractionFree || _window == null)
            {
                DistractionFreePadding = new Thickness(0);
                return;
            }

            var windowWidth = _window.ActualWidth;
            if (windowWidth <= _maxReadingWidth)
            {
                // Window is narrower than reading width; no padding
                DistractionFreePadding = new Thickness(0);
            }
            else
            {
                var horizontalPad = (windowWidth - _maxReadingWidth) / 2;
                DistractionFreePadding = new Thickness(horizontalPad, 0, horizontalPad, 0);
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
