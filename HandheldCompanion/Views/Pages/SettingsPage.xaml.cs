﻿using ControllerCommon;
using Microsoft.Extensions.Logging;
using ModernWpf;
using System;
using System.Linq;
using System.ServiceProcess;
using System.Windows.Controls;
using ServiceControllerStatus = ControllerCommon.ServiceControllerStatus;

namespace HandheldCompanion.Views.Pages
{
    /// <summary>
    /// Interaction logic for SettingsPage.xaml
    /// </summary>
    public partial class SettingsPage : Page
    {
        private MainWindow mainWindow;
        private ILogger microsoftLogger;
        private PipeClient pipeClient;
        private ServiceManager serviceManager;

        // settings vars
        public bool s_ToastEnable, s_RunAtStartup, s_StartMinimized, s_CloseMinimises;
        public int s_ApplicationTheme, s_ServiceStartup;

        public event ToastChangedEventHandler ToastChanged;
        public delegate void ToastChangedEventHandler(bool value);

        public event AutoStartChangedEventHandler AutoStartChanged;
        public delegate void AutoStartChangedEventHandler(bool value);

        public event ServiceChangedEventHandler ServiceChanged;
        public delegate void ServiceChangedEventHandler(ServiceStartMode value);

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
        }

        public SettingsPage()
        {
            InitializeComponent();

            Toggle_AutoStart.IsOn = s_RunAtStartup = Properties.Settings.Default.RunAtStartup;
            Toggle_Background.IsOn = s_StartMinimized = Properties.Settings.Default.StartMinimized;
            Toggle_CloseMinimizes.IsOn = s_CloseMinimises = Properties.Settings.Default.CloseMinimises;

            cB_Theme.SelectedIndex = s_ApplicationTheme = Properties.Settings.Default.MainWindowTheme;

            Toggle_Notification.IsOn = s_ToastEnable = Properties.Settings.Default.ToastEnable;
        }

        public SettingsPage(string Tag, MainWindow mainWindow, ILogger microsoftLogger) : this()
        {
            this.Tag = Tag;
            this.mainWindow = mainWindow;
            this.microsoftLogger = microsoftLogger;

            this.pipeClient = mainWindow.pipeClient;
            this.serviceManager = mainWindow.serviceManager;
            this.serviceManager.Updated += OnServiceUpdate;

            foreach (ServiceStartMode mode in ((ServiceStartMode[])Enum.GetValues(typeof(ServiceStartMode))).Where(mode => mode >= ServiceStartMode.Automatic))
                cB_StartupType.Items.Add(mode);
        }

        private void Toggle_AutoStart_Toggled(object sender, System.Windows.RoutedEventArgs e)
        {
            Properties.Settings.Default.RunAtStartup = Toggle_AutoStart.IsOn;
            Properties.Settings.Default.Save();

            s_RunAtStartup = Toggle_AutoStart.IsOn;
            AutoStartChanged?.Invoke(s_RunAtStartup);
        }

        private void Toggle_Background_Toggled(object sender, System.Windows.RoutedEventArgs e)
        {
            Properties.Settings.Default.StartMinimized = Toggle_Background.IsOn;
            Properties.Settings.Default.Save();

            s_StartMinimized = Toggle_Background.IsOn;
        }

        private void cB_StartupType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ServiceStartMode mode;
            switch (cB_StartupType.SelectedIndex)
            {
                case 0:
                    mode = ServiceStartMode.Automatic;
                    break;
                default:
                case 1:
                    mode = ServiceStartMode.Manual;
                    break;
                case 2:
                    mode = ServiceStartMode.Disabled;
                    break;
            }
            ServiceChanged?.Invoke(mode);
        }

        private void Toggle_CloseMinimizes_Toggled(object sender, System.Windows.RoutedEventArgs e)
        {
            Properties.Settings.Default.CloseMinimises = Toggle_CloseMinimizes.IsOn;
            Properties.Settings.Default.Save();

            s_CloseMinimises = Toggle_CloseMinimizes.IsOn;
        }

        private void Toggle_Notification_Toggled(object sender, System.Windows.RoutedEventArgs e)
        {
            Properties.Settings.Default.ToastEnable = Toggle_Notification.IsOn;
            Properties.Settings.Default.Save();

            s_ToastEnable = Toggle_Notification.IsOn;
            ToastChanged?.Invoke(s_ToastEnable);
        }

        private void cB_Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.MainWindowTheme = cB_Theme.SelectedIndex;
            Properties.Settings.Default.Save();

            ApplyTheme((ApplicationTheme)cB_Theme.SelectedIndex);
        }

        public void ApplyTheme(ApplicationTheme Theme)
        {
            ThemeManager.Current.ApplicationTheme = Theme;
        }

        #region serviceManager
        private void OnServiceUpdate(ServiceControllerStatus status, int mode)
        {
            this.Dispatcher.Invoke(() =>
            {
                switch (status)
                {
                    case ServiceControllerStatus.Paused:
                    case ServiceControllerStatus.Stopped:
                    case ServiceControllerStatus.Running:
                    case ServiceControllerStatus.ContinuePending:
                    case ServiceControllerStatus.PausePending:
                    case ServiceControllerStatus.StartPending:
                    case ServiceControllerStatus.StopPending:
                        cB_StartupType.IsEnabled = true;
                        break;
                    default:
                        cB_StartupType.IsEnabled = false;
                        break;
                }

                if (mode != -1)
                {
                    ServiceStartMode serviceMode = (ServiceStartMode)mode;
                    cB_StartupType.SelectedItem = serviceMode;
                }
            });
        }
        #endregion
    }
}