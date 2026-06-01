using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IptvPlayer.Services;
using IptvPlayer.ViewModels;

namespace IptvPlayer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // WinForms panel that hosts the native video surface (its HWND is given to libvlc).
    private readonly System.Windows.Forms.Panel _videoPanel = new()
    {
        BackColor = System.Drawing.Color.Black,
        Dock = System.Windows.Forms.DockStyle.Fill
    };

    // Saved window chrome for fullscreen restore.
    private WindowStyle _prevStyle;
    private WindowState _prevState;
    private ResizeMode _prevResize;
    private bool _isFullscreen;

    public MainWindow()
    {
        InitializeComponent();

        // Simple manual composition; swap for a DI container in a larger app.
        var favorites = new FavoritesService();
        var loader = new PlaylistLoader();
        var settings = new SettingsService();

        _vm = new MainViewModel(favorites, loader, settings);
        DataContext = _vm;

        // Host the video panel and give libvlc its HWND once the handle exists.
        VideoHost.Child = _videoPanel;
        if (_videoPanel.IsHandleCreated) _vm.SetVideoHandle(_videoPanel.Handle);
        else _videoPanel.HandleCreated += (_, __) => _vm.SetVideoHandle(_videoPanel.Handle);

        // Wheel over the video adjusts volume (the WinForms region never reaches WPF's
        // PreviewMouseWheel). Focus on enter so the panel actually receives wheel events.
        _videoPanel.MouseEnter += (_, __) => _videoPanel.Focus();
        _videoPanel.MouseWheel += (_, e) => AdjustVolumeByWheel(e.Delta);

        Loaded += async (_, __) => await _vm.InitializeAsync();
        Closed += OnClosedHandler;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    // Vertical mouse-wheel adjusts VOLUME, EXCEPT while the pointer is over the channel
    // list (there the wheel scrolls the list as usual). Wheel up = louder, down = quieter.
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsWithinChannelList(e.OriginalSource as DependencyObject)) return;

        AdjustVolumeByWheel(e.Delta);
        e.Handled = true;
    }

    // Wheel up = volume up, wheel down = volume down.
    private void AdjustVolumeByWheel(int delta)
    {
        if (delta > 0) _vm.VolumeUpCommand.Execute(null);
        else if (delta < 0) _vm.VolumeDownCommand.Execute(null);
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private bool IsWithinChannelList(DependencyObject? src)
    {
        while (src is not null)
        {
            if (ReferenceEquals(src, ChannelList)) return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    // Keep the selected channel scrolled into view when it changes via keyboard
    // (Up/Down) or the Prev/Next transport buttons, so the highlight never scrolls
    // off-screen and the user always sees what is currently playing.
    private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is not null)
            ChannelList.ScrollIntoView(ChannelList.SelectedItem);
    }

    private void OnClosedHandler(object? sender, EventArgs e)
    {
        // Detach the native video surface before tearing the player down.
        _vm.SetVideoHandle(IntPtr.Zero);
        _vm.Dispose();
    }

    // We intercept at PreviewKeyDown (tunneling) so arrows reach us BEFORE a
    // focused ListBox/Slider consumes them.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:    _vm.PrevChannelCommand.Execute(null); e.Handled = true; break;
            case Key.Down:  _vm.NextChannelCommand.Execute(null); e.Handled = true; break;
            case Key.Left:  _vm.VolumeDownCommand.Execute(null);  e.Handled = true; break;
            case Key.Right: _vm.VolumeUpCommand.Execute(null);    e.Handled = true; break;
            case Key.Space: _vm.PlayPauseCommand.Execute(null);   e.Handled = true; break;
            case Key.F11:   ToggleFullscreen();                   e.Handled = true; break;
            case Key.Escape:
                // Esc only exits fullscreen (does nothing in windowed mode).
                if (_isFullscreen) { ToggleFullscreen(); e.Handled = true; }
                break;
        }
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            // Snapshot current chrome so we can restore it exactly.
            _prevStyle  = WindowStyle;
            _prevState  = WindowState;
            _prevResize = ResizeMode;

            // Order matters. Drop the chrome, force Normal, then Maximize:
            // WPF re-evaluates which monitor the window is on at the moment it
            // maximizes, so a borderless-maximized window covers exactly the
            // CURRENT monitor (multi-monitor safe, including mixed DPI).
            WindowStyle = WindowStyle.None;
            ResizeMode  = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;

            // Hide the top toolbar and bottom transport bar for a clean fullscreen.
            TopBar.Visibility = Visibility.Collapsed;
            TransportBar.Visibility = Visibility.Collapsed;

            _isFullscreen = true;
        }
        else
        {
            // Restore precisely what we saved.
            WindowStyle = _prevStyle;
            ResizeMode  = _prevResize;
            WindowState = _prevState;

            TopBar.Visibility = Visibility.Visible;
            TransportBar.Visibility = Visibility.Visible;

            _isFullscreen = false;
        }
    }
}
