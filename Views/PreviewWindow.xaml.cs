using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FlightSaver.Models;
using FlightSaver.Rendering;
using FlightSaver.Services;

namespace FlightSaver.Views;

public partial class PreviewWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    public PreviewWindow(Config config, FlightTracker tracker, IntPtr parentHandle)
    {
        InitializeComponent();
        var radar = new RadarCanvas(config, tracker);
        RootGrid.Children.Add(radar);

        SourceInitialized += (_, _) =>
        {
            var helper = new WindowInteropHelper(this);
            var hwnd = helper.Handle;

            if (GetClientRect(parentHandle, out var rect))
            {
                Width = rect.Right - rect.Left;
                Height = rect.Bottom - rect.Top;
            }

            var style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style | WS_CHILD);
            SetParent(hwnd, parentHandle);
            Left = 0;
            Top = 0;
        };
    }
}
