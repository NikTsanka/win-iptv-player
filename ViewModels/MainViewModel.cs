using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Models;
using IptvPlayer.Services;
using LibVLCSharp.Shared;

namespace IptvPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const string AllGroups = "All / ყველა / Все";

    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private readonly IFavoritesService _favorites;
    private readonly PlaylistLoader _loader;
    private readonly SettingsService _settings;
    private AppSettings _appSettings = new();
    private Media? _currentMedia;
    private bool _disposed;

    // Debounce ("zapping"): rapid channel changes (mouse wheel / held arrows) must not
    // each trigger a heavy Play on the UI thread — that floods it and freezes the app.
    // We coalesce them and only start the LAST selected channel once changes settle.
    private Channel? _zapTarget;
    private readonly DispatcherTimer _zapTimer =
        new() { Interval = TimeSpan.FromMilliseconds(350) };

    // Serialized background playback worker. Channel switching tears down the previous
    // stream (a BLOCKING native call) off the UI thread, then issues Play() back on the
    // UI thread. Only the latest requested channel is played (coalescing).
    private readonly object _playGate = new();
    private Channel? _pendingPlay;
    private bool _playWorkerActive;

    public MediaPlayer MediaPlayer => _mediaPlayer;

    // Hand libvlc the native window handle of the WinForms panel that hosts the video.
    // Direct HWND embedding renders straight into the child window (no DWM thumbnail).
    public void SetVideoHandle(IntPtr hwnd)
    {
        _mediaPlayer.Hwnd = hwnd;
        Log.Write($"SetVideoHandle hwnd=0x{hwnd.ToInt64():X}");
    }

    public ObservableCollection<Channel> AllChannels { get; } = new();
    public ObservableCollection<string> Groups { get; } = new();
    public ICollectionView ChannelsView { get; }

    [ObservableProperty] private string? selectedGroup;
    [ObservableProperty] private Channel? selectedChannel;
    [ObservableProperty] private bool showFavoritesOnly;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private string? playlistUrl;
    [ObservableProperty] private string? searchText;

    public MainViewModel(IFavoritesService favorites, PlaylistLoader loader, SettingsService settings)
    {
        _favorites = favorites;
        _loader = loader;
        _settings = settings;

        Log.Clear();
        Log.Write("VM ctor: creating LibVLC + MediaPlayer");

        // Core.Initialize() already ran in App.OnStartup -> safe to construct LibVLC now.
        // --vout=directdraw renders straight into the embedded HWND (no DXGI swapchain),
        // which the direct3d11 vout fails to composite over this GPU/airspace combo.
        _libVLC = new LibVLC(
            "--vout=directdraw",
            "--no-snapshot-preview");
        _libVLC.Log += OnVlcNativeLog;     // DIAGNOSTIC: capture vout / decoder messages
        _mediaPlayer = new MediaPlayer(_libVLC);
        WireMediaPlayerEvents();

        // CollectionView gives us live group + favorites filtering over one source list.
        ChannelsView = CollectionViewSource.GetDefaultView(AllChannels);
        ChannelsView.Filter = FilterChannel;

        // When the debounce window elapses, play whatever channel is the latest target.
        _zapTimer.Tick += (_, __) =>
        {
            _zapTimer.Stop();
            Log.Write($"zap timer tick -> target #{_zapTarget?.Number} {_zapTarget?.Name}");
            if (_zapTarget is not null) Play(_zapTarget);
        };
    }

    public async Task InitializeAsync()
    {
        _appSettings = _settings.Load();
        await _favorites.LoadAsync();

        Volume = _appSettings.Volume;          // pushes into MediaPlayer.Volume
        AspectRatio = _appSettings.AspectRatio;
        PlaylistUrl = _appSettings.LastPlaylistUrl;

        // Auto-load the last-used URL playlist on startup, if one was saved.
        if (!string.IsNullOrWhiteSpace(PlaylistUrl) && LoadUrlCommand.CanExecute(null))
            await LoadUrlCommand.ExecuteAsync(null);
    }

    // ---- Volume (manual property: must mirror into MediaPlayer and back) -------------
    private int _volume = 80;
    public int Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _volume, clamped))
            {
                _mediaPlayer.Volume = clamped;          // 3.8.x: int 0..100
                _appSettings.Volume = clamped;
            }
        }
    }

    // ---- Aspect ratio ----------------------------------------------------------------
    private string _aspectRatio = "Auto";
    public string AspectRatio
    {
        get => _aspectRatio;
        set
        {
            if (SetProperty(ref _aspectRatio, value))
            {
                // null tells libvlc to use the source's native ratio.
                _mediaPlayer.AspectRatio = value == "Auto" ? null : value;
                _appSettings.AspectRatio = value;
            }
        }
    }

    // ---- MediaPlayer events fire on a LibVLC worker thread; marshal to UI ------------
    private void WireMediaPlayerEvents()
    {
        _mediaPlayer.Playing          += (_, __) => { Log.Write("VLC evt: Playing");          OnUi(() => { IsPlaying = true;  StatusText = "Playing / დაკვრა"; }); };
        _mediaPlayer.Paused           += (_, __) => { Log.Write("VLC evt: Paused");           OnUi(() => { IsPlaying = false; StatusText = "Paused"; }); };
        _mediaPlayer.Stopped          += (_, __) => { Log.Write("VLC evt: Stopped");          OnUi(() => { IsPlaying = false; StatusText = "Stopped"; }); };
        _mediaPlayer.EndReached       += (_, __) => { Log.Write("VLC evt: EndReached");        OnUi(() => { IsPlaying = false; StatusText = "End of stream"; }); };
        _mediaPlayer.EncounteredError += (_, __) => { Log.Write("VLC evt: EncounteredError");  OnUi(() => { IsPlaying = false; StatusText = "Playback error"; }); };

        // DIAGNOSTIC: Vout fires when the number of video outputs changes. count>0 means
        // a video surface was created; if it stays 0 the channel is audio-only (no vout).
        _mediaPlayer.Vout += (_, e) => Log.Write($"VLC evt: Vout count={e.Count}  videoTracks={_mediaPlayer.VideoTrackCount}");

        // VLC reports volume as a normalized float (1.0 == 100%). Reflect external
        // changes back into the slider, guarding against the set-loop we triggered.
        _mediaPlayer.VolumeChanged += (_, e) => OnUi(() =>
        {
            var v = Math.Clamp((int)Math.Round(e.Volume * 100), 0, 100);
            if (v != _volume) { _volume = v; OnPropertyChanged(nameof(Volume)); }
        });
    }

    private static void OnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    // DIAGNOSTIC: keep only the signal — warnings/errors plus anything about the video
    // output, hardware decoding or HEVC. This tells us WHY a 4K channel is audio-only.
    private static void OnVlcNativeLog(object? sender, LogEventArgs e)
    {
        try
        {
            var m = e.Message ?? string.Empty;
            var keep = e.Level >= LogLevel.Warning
                || m.Contains("vout", StringComparison.OrdinalIgnoreCase)
                || m.Contains("hevc", StringComparison.OrdinalIgnoreCase)
                || m.Contains("h265", StringComparison.OrdinalIgnoreCase)
                || m.Contains("decoder", StringComparison.OrdinalIgnoreCase)
                || m.Contains("hardware", StringComparison.OrdinalIgnoreCase)
                || m.Contains("dxva", StringComparison.OrdinalIgnoreCase)
                || m.Contains("direct3d", StringComparison.OrdinalIgnoreCase)
                || m.Contains("d3d11", StringComparison.OrdinalIgnoreCase);
            if (keep)
                Log.Write($"VLCLOG [{e.Level}] {e.Module}: {m}");
        }
        catch { /* never throw from a log callback */ }
    }

    // ---- Filtering -------------------------------------------------------------------
    private bool FilterChannel(object obj)
    {
        if (obj is not Channel c) return false;
        if (ShowFavoritesOnly && !_favorites.IsFavorite(c)) return false;
        if (!string.IsNullOrEmpty(SelectedGroup) && SelectedGroup != AllGroups &&
            !string.Equals(c.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            c.Name.IndexOf(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return true;
    }

    partial void OnSelectedGroupChanged(string? value) => ChannelsView.Refresh();
    partial void OnShowFavoritesOnlyChanged(bool value) => ChannelsView.Refresh();
    partial void OnSearchTextChanged(string? value) => ChannelsView.Refresh();
    partial void OnSelectedChannelChanged(Channel? value)
    {
        OnPropertyChanged(nameof(IsCurrentFavorite));
        if (value is null) return;

        Log.Write($"selection changed -> #{value.Number} {value.Name}");

        // Don't play immediately — restart the debounce window. While the user keeps
        // wheeling/holding arrows the timer keeps resetting, so Play runs only once
        // (on the final channel) after they pause. This is what prevents the freeze.
        _zapTarget = value;
        _zapTimer.Stop();
        _zapTimer.Start();
    }

    public bool IsCurrentFavorite =>
        SelectedChannel is not null && _favorites.IsFavorite(SelectedChannel);

    // ---- Playlist loading ------------------------------------------------------------
    [RelayCommand]
    private async Task LoadFileAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            // Assumption: a built-in file dialog is acceptable here; in a larger app
            // this would sit behind an IDialogService for testability.
            Filter = "Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            StatusText = "Loading file...";
            ApplyPlaylist(await _loader.LoadFromFileAsync(dlg.FileName));
            _appSettings.LastPlaylistPath = dlg.FileName;
        }
        catch (Exception ex) { StatusText = "Load failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task LoadUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(PlaylistUrl)) return;
        try
        {
            StatusText = "Loading URL...";
            ApplyPlaylist(await _loader.LoadFromUrlAsync(PlaylistUrl.Trim()));
            _appSettings.LastPlaylistUrl = PlaylistUrl.Trim();
        }
        catch (Exception ex) { StatusText = "Load failed: " + ex.Message; }
    }

    private void ApplyPlaylist(Playlist playlist)
    {
        AllChannels.Clear();
        var n = 1;
        foreach (var c in playlist.Channels)
        {
            c.Number = n++;                // absolute 1-based channel number
            AllChannels.Add(c);
        }

        Groups.Clear();
        Groups.Add(AllGroups);
        foreach (var g in playlist.Groups) Groups.Add(g);

        SelectedGroup = AllGroups;     // also refreshes the view
        StatusText = $"{AllChannels.Count} channels loaded";
    }

    // ---- Transport -------------------------------------------------------------------
    // Called on the UI thread (from the debounce timer). Hands the channel to the
    // serialized background worker instead of playing inline — because the inline
    // MediaPlayer.Play() -> set_Media call BLOCKS the UI thread while it stops the
    // previous heavy 4K stream (confirmed by a dotnet-stack capture of the freeze).
    private void Play(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.StreamUrl)) return;

        StatusText = $"> {channel.Name}";

        lock (_playGate)
        {
            _pendingPlay = channel;       // newest request wins (coalescing)
            if (_playWorkerActive) return;
            _playWorkerActive = true;
        }
        Task.Run(PlayWorkerLoop);
    }

    private void PlayWorkerLoop()
    {
        while (true)
        {
            if (_disposed) { lock (_playGate) { _playWorkerActive = false; } return; }

            Channel target;
            lock (_playGate)
            {
                if (_pendingPlay is null) { _playWorkerActive = false; return; }
                target = _pendingPlay;
                _pendingPlay = null;
            }

            try
            {
                // 1) BLOCKING teardown of the current stream — safe on this bg thread.
                Log.Write($"worker: Stop() before #{target.Number} {target.Name}");
                _mediaPlayer.Stop();
                Log.Write("worker: Stop() returned");

                var prev = Interlocked.Exchange(ref _currentMedia, null);
                prev?.Dispose();

                // 2) Build the new media (cheap).
                var media = Uri.TryCreate(target.StreamUrl, UriKind.Absolute, out var uri)
                    ? new Media(_libVLC, uri)
                    : new Media(_libVLC, target.StreamUrl, FromType.FromPath);
                media.AddOption(":network-caching=1500");

                // 3) Play on the UI thread (required for the VideoView to render). After
                //    the Stop() above there is nothing to tear down, so it returns fast.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null) { media.Dispose(); return; }

                Log.Write("worker: invoking Play() on UI thread");
                dispatcher.Invoke(() =>
                {
                    if (_disposed) { media.Dispose(); return; }
                    _mediaPlayer.Play(media);
                    _currentMedia = media;
                });
                Log.Write("worker: Play() issued on UI thread");
            }
            catch (Exception ex)
            {
                Log.Write("worker: ERROR " + ex);
            }
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause();
        else if (_mediaPlayer.Media is not null) _mediaPlayer.Play();
        else if (SelectedChannel is not null) Play(SelectedChannel);
    }

    [RelayCommand]
    private void Stop() => Task.Run(() => { try { _mediaPlayer.Stop(); } catch { /* ignore */ } });

    [RelayCommand]
    private void NextChannel() => Step(+1);

    [RelayCommand]
    private void PrevChannel() => Step(-1);

    private void Step(int delta)
    {
        // Step through the *filtered* order the user actually sees.
        var list = ChannelsView.Cast<Channel>().ToList();
        if (list.Count == 0) return;

        var idx = SelectedChannel is null ? -1 : list.IndexOf(SelectedChannel);
        idx = (idx + delta + list.Count) % list.Count;
        SelectedChannel = list[idx];           // triggers Play via OnSelectedChannelChanged
    }

    [RelayCommand] private void VolumeUp()   => Volume += 5;
    [RelayCommand] private void VolumeDown() => Volume -= 5;
    [RelayCommand] private void SetAspect(string ratio) => AspectRatio = ratio;

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedChannel is null) return;
        await _favorites.ToggleAsync(SelectedChannel);
        OnPropertyChanged(nameof(IsCurrentFavorite));
        if (ShowFavoritesOnly) ChannelsView.Refresh();
    }

    // ---- Deterministic native cleanup ------------------------------------------------
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _zapTimer.Stop();

        // Do NOT call MediaPlayer.Stop() here — it is a blocking native call and can
        // deadlock on shutdown. Disposing the player tears playback down safely.
        _mediaPlayer.Dispose();
        _currentMedia?.Dispose();
        _libVLC.Dispose();

        _settings.Save(_appSettings);    // persist last-used volume / aspect / sources
    }
}
