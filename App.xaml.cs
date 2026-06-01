using System;
using System.Windows;
using System.Windows.Threading;
using IptvPlayer.Services;
using LibVLCSharp.Shared;

namespace IptvPlayer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Capture any unhandled crash so the debug log shows it (diagnostics).
        DispatcherUnhandledException += (_, args) =>
            Log.Write("FATAL DispatcherUnhandledException: " + args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("FATAL UnhandledException: " + args.ExceptionObject);

        // Loads the native libvlc binaries shipped by VideoLAN.LibVLC.Windows.
        // MUST run exactly once, before ANY LibVLC/MediaPlayer is constructed.
        // 3.8.x type is LibVLCSharp.Shared.Core (NOT LibVLCSharp.Core).
        Core.Initialize();
    }
}
