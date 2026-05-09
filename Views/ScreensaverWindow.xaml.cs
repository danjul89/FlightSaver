using System;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FlightSaver.Models;
using FlightSaver.Rendering;
using FlightSaver.Services;

namespace FlightSaver.Views;

public partial class ScreensaverWindow : Window
{
    private System.Windows.Point? _initialMousePos;

    public ScreensaverWindow(Config config, FlightTracker tracker, Rectangle screenBounds)
    {
        InitializeComponent();

        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = screenBounds.Left / dpi.DpiScaleX;
            Top = screenBounds.Top / dpi.DpiScaleY;
            Width = screenBounds.Width / dpi.DpiScaleX;
            Height = screenBounds.Height / dpi.DpiScaleY;
        };

        var radar = new RadarCanvas(config, tracker);
        RootGrid.Children.Add(radar);

        PreviewMouseMove += OnMouseMove;
        PreviewMouseDown += OnMouseDown;
        PreviewKeyDown += OnKeyDown;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_initialMousePos is null) { _initialMousePos = pos; return; }
        var dx = pos.X - _initialMousePos.Value.X;
        var dy = pos.Y - _initialMousePos.Value.Y;
        if (Math.Abs(dx) > 4 || Math.Abs(dy) > 4) ExitApp();
    }

    private void OnMouseDown(object? sender, MouseButtonEventArgs e) => ExitApp();
    private void OnKeyDown(object? sender, KeyEventArgs e) => ExitApp();

    private static void ExitApp() => System.Windows.Application.Current.Shutdown();
}
