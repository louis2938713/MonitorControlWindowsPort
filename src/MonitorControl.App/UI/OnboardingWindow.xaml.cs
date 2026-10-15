using System.Windows;
using MonitorControl.App.Platform;

namespace MonitorControl.App.UI;

/// <summary>First launch window. Port of OnboardingViewController.swift (no permission step on Windows).</summary>
public partial class OnboardingWindow
{
    public OnboardingWindow()
    {
        InitializeComponent();
        StartAtLogin.IsChecked = StartupRegistration.IsEnabled;
    }

    private void OnStartAtLoginClick(object sender, RoutedEventArgs e) => StartupRegistration.Set(StartAtLogin.IsChecked == true);

    private void OnStartClick(object sender, RoutedEventArgs e) => Close();
}
