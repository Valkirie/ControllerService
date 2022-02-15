﻿using ControllerCommon;
using Microsoft.Extensions.Logging;
using ModernWpf.Controls;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml;
using Page = System.Windows.Controls.Page;

namespace ControllerHelperWPF
{
    /// <summary>
    /// Interaction logic for Profiles.xaml
    /// </summary>
    public partial class ProfileListPage : Page
    {
        private MainWindow mainWindow;
        private ILogger microsoftLogger;

        private ProfileManager profileManager;
        private Profile profileCurrent;

        // pipe vars
        PipeClient pipeClient;

        public ProfileListPage()
        {
            InitializeComponent();
        }

        public ProfileListPage(MainWindow mainWindow, ILogger microsoftLogger) : this()
        {
            this.mainWindow = mainWindow;
            this.microsoftLogger = microsoftLogger;

            this.pipeClient = mainWindow.pipeClient;
            this.profileManager = mainWindow.profileManager;

            // initialize Profile Manager
            profileManager.Deleted += ProfileDeleted;
            profileManager.Updated += ProfileUpdated;
            profileManager.Start();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // select default profile
            cB_Profiles.SelectedItem = profileManager.GetDefault();
        }

        #region UI
        public void ProfileUpdated(Profile profile)
        {
            // inform Service we have a new default profile
            if (profile.IsDefault)
                pipeClient.SendMessage(new PipeClientProfile() { profile = profile });

            this.Dispatcher.Invoke(() =>
            {
                int idx = cB_Profiles.Items.IndexOf(profile);

                foreach (Profile pr in cB_Profiles.Items)
                    if (pr.path == profile.path)
                    {
                        // IndexOf will always fail !
                        idx = cB_Profiles.Items.IndexOf(pr);
                        break;
                    }

                if (idx == -1)
                    cB_Profiles.Items.Add(profile);
                else
                    cB_Profiles.Items[idx] = profile;

                /* clone template
                string gridXaml = XamlWriter.Save(Button_Template);
                
                StringReader stringReader = new StringReader(gridXaml);
                XmlReader xmlReader = XmlReader.Create(stringReader);

                Button ProfileButton = (Button)XamlReader.Load(xmlReader);

                // update template before copy
                ProfileButton.Visibility = Visibility.Visible;
                ((TextBlock)ProfileButton.FindName("ProfileName")).Text = profile.name;
                ((TextBlock)ProfileButton.FindName("ProfilePath")).Text = profile.path;
                ((TextBlock)ProfileButton.FindName("ProfileKey")).Text = profile.name.Substring(0,1);

                StackPanel_Profiles.Children.Add(ProfileButton); */
            });
        }

        public void ProfileDeleted(Profile profile)
        {
            this.Dispatcher.Invoke(() =>
            {
                // todo
            });
        }
        #endregion

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            // todo
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            // todo
            mainWindow.ContentFrame.Navigate(typeof(ProfileSettingsPage));
        }

        private void cB_Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            profileCurrent = (Profile)cB_Profiles.SelectedItem;

            if (profileCurrent == null)
                return;

            this.Dispatcher.Invoke(() =>
            {
                // disable button if is default profile
                b_DeleteProfile.IsEnabled = !profileCurrent.IsDefault;

                tB_ProfileName.Text = profileCurrent.name;
                tB_ProfilePath.Text = profileCurrent.path;
                cB_Whitelist.IsChecked = profileCurrent.whitelisted;
                cB_Wrapper.IsChecked = profileCurrent.use_wrapper;
                cB_UniversalMotion.IsChecked = profileCurrent.umc_enabled;

                tb_ProfileGyroValue.Value = profileCurrent.gyrometer;
                tb_ProfileAcceleroValue.Value = profileCurrent.accelerometer;

                cB_GyroSteering.SelectedIndex = profileCurrent.steering;

                cB_InvertVertical.IsChecked = profileCurrent.invertvertical;
                cB_InvertHorizontal.IsChecked = profileCurrent.inverthorizontal;
            });
        }

        private void b_DeleteProfile_Click(object sender, RoutedEventArgs e)
        {

        }

        private void b_ApplyProfile_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}