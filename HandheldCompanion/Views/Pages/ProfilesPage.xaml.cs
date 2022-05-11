using ControllerCommon;
using ControllerCommon.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ModernWpf.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using GamepadButtonFlagsExt = ControllerCommon.Utils.GamepadButtonFlagsExt;
using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.Pages
{
    /// <summary>
    /// Interaction logic for Profiles.xaml
    /// </summary>
    public partial class ProfilesPage : Page
    {
        private MainWindow mainWindow;
        private ILogger logger;

        private ProfileManager profileManager;
        private Profile profileCurrent;

        private Dictionary<GamepadButtonFlagsExt, CheckBox> activators = new();

        // pipe vars
        PipeClient pipeClient;

        public ProfilesPage()
        {
            InitializeComponent();
        }

        public ProfilesPage(string Tag, MainWindow mainWindow, ILogger logger) : this()
        {
            this.Tag = Tag;

            this.mainWindow = mainWindow;
            this.logger = logger;

            this.pipeClient = mainWindow.pipeClient;
            this.pipeClient.ServerMessage += PipeClient_ServerMessage;

            this.profileManager = mainWindow.profileManager;

            // initialize Profile Manager
            profileManager.Deleted += ProfileDeleted;
            profileManager.Updated += ProfileUpdated;
            profileManager.Loaded += ProfileLoaded;

            // draw buttons
            foreach (GamepadButtonFlagsExt button in (GamepadButtonFlagsExt[])Enum.GetValues(typeof(GamepadButtonFlagsExt)))
            {
                // create panel
                SimpleStackPanel panel = new SimpleStackPanel() { Spacing = 6, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                // create icon
                FontIcon icon = new FontIcon() { Glyph = "" };
                icon.Glyph = InputUtils.GamepadButtonToGlyph(button);

                if (icon.Glyph != "")
                    panel.Children.Add(icon);

                // create textblock
                string description = EnumUtils.GetDescriptionFromEnumValue(button);
                TextBlock text = new TextBlock() { Text = description };
                panel.Children.Add(text);

                // create checkbox
                CheckBox checkbox = new CheckBox() { Tag = button, Content = panel, Width = 170 };
                cB_Buttons.Children.Add(checkbox);
                activators.Add(button, checkbox);
            }

            foreach (Input mode in (Input[])Enum.GetValues(typeof(Input)))
            {
                // create panel
                SimpleStackPanel panel = new SimpleStackPanel() { Spacing = 6, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                // create icon
                FontIcon icon = new FontIcon() { Glyph = "" };

                switch(mode)
                {
                    default:
                    case Input.PlayerSpace:
                        icon.Glyph = "\uF119";
                        break;
                    case Input.JoystickCamera:
                        icon.Glyph = "\uE714";
                        break;
                    case Input.JoystickSteering:
                        icon.Glyph = "\uEC47";
                        break;
                }

                if (icon.Glyph != "")
                    panel.Children.Add(icon);

                // create textblock
                string description = EnumUtils.GetDescriptionFromEnumValue(mode);
                TextBlock text = new TextBlock() { Text = description };
                panel.Children.Add(text);

                cB_Input.Items.Add(panel);
            }

            foreach (Output mode in (Output[])Enum.GetValues(typeof(Output)))
            {
                // create panel
                SimpleStackPanel panel = new SimpleStackPanel() { Spacing = 6, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                // create icon
                FontIcon icon = new FontIcon() { Glyph = "" };

                switch (mode)
                {
                    default:
                    case Output.RightStick:
                        icon.Glyph = "\uF109";
                        break;
                    case Output.LeftStick:
                        icon.Glyph = "\uF108";
                        break;
                }

                if (icon.Glyph != "")
                    panel.Children.Add(icon);

                // create textblock
                string description = EnumUtils.GetDescriptionFromEnumValue(mode);
                TextBlock text = new TextBlock() { Text = description };
                panel.Children.Add(text);

                cB_Output.Items.Add(panel);
            }
        }

        private void PipeClient_ServerMessage(object sender, PipeMessage e)
        {
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
        }

        #region UI
        public void ProfileUpdated(Profile profile, bool backgroundtask)
        {
            // inform Service we have a new default profile
            if (profile.isDefault)
                pipeClient.SendMessage(new PipeClientProfile() { profile = profile });

            this.Dispatcher.Invoke(async () =>
            {
                int idx = -1;
                foreach (Profile pr in cB_Profiles.Items)
                    if (pr.executable == profile.executable)
                    {
                        idx = cB_Profiles.Items.IndexOf(pr);
                        break;
                    }

                if (idx != -1)
                    cB_Profiles.Items[idx] = profile;
                else
                    cB_Profiles.Items.Add(profile);

                cB_Profiles.SelectedItem = profile;
            });
        }

        public void ProfileDeleted(Profile profile)
        {
            this.Dispatcher.Invoke(() =>
            {
                int idx = -1;
                foreach (Profile pr in cB_Profiles.Items)
                    if (pr.executable == profile.executable)
                    {
                        idx = cB_Profiles.Items.IndexOf(pr);
                        break;
                    }
                cB_Profiles.Items.RemoveAt(idx);
            });
        }

        private void ProfileLoaded()
        {
            cB_Profiles.SelectedItem = profileManager.GetDefault();
        }
        #endregion

        private async void b_CreateProfile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var path = openFileDialog.FileName;
                    var folder = Path.GetDirectoryName(path);

                    var file = openFileDialog.SafeFileName;
                    var ext = Path.GetExtension(file);

                    switch (ext)
                    {
                        default:
                        case ".exe":
                            break;
                        case ".xml":
                            try
                            {
                                XmlDocument doc = new XmlDocument();
                                doc.Load(path);

                                XmlNodeList Applications = doc.GetElementsByTagName("Applications");
                                foreach (XmlNode node in Applications)
                                {
                                    foreach (XmlNode child in node.ChildNodes)
                                    {
                                        if (child.Name.Equals("Application"))
                                        {
                                            if (child.Attributes != null)
                                            {
                                                foreach (XmlAttribute attribute in child.Attributes)
                                                {
                                                    switch (attribute.Name)
                                                    {
                                                        case "Executable":
                                                            path = Path.Combine(folder, attribute.InnerText);
                                                            file = Path.GetFileName(path);
                                                            break;
                                                    }
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex.Message, true);
                            }
                            break;
                    }

                    Profile profile = new Profile(path);

                    bool exists = false;

                    if (profileManager.Contains(profile))
                    {
                        Task<ContentDialogResult> result = Dialog.ShowAsync($"{Properties.Resources.ProfilesPage_AreYouSureOverwrite1} \"{profile.name}\"?", 
                                                                            $"{Properties.Resources.ProfilesPage_AreYouSureOverwrite2}", 
                                                                            ContentDialogButton.Primary, 
                                                                            $"{Properties.Resources.ProfilesPage_Cancel}", 
                                                                            $"{Properties.Resources.ProfilesPage_Yes}");
                        await result; // sync call

                        switch (result.Result)
                        {
                            case ContentDialogResult.Primary:
                                exists = false;
                                break;
                            default:
                                exists = true;
                                break;
                        }
                    }

                    if (!exists)
                    {
                        profileManager.UpdateOrCreateProfile(profile, false);
                        profileManager.SerializeProfile(profile);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                }
            }
        }

        private void b_AdditionalSettings_Click(object sender, RoutedEventArgs e)
        {
            if (profileCurrent == null)
                return;

            Page page;
            switch ((Input)cB_Input.SelectedIndex)
            {
                default:
                case Input.JoystickCamera:
                case Input.PlayerSpace:
                    page = new ProfileSettingsMode0("ProfileSettingsMode0", profileCurrent, pipeClient, logger);
                    break;
                case Input.JoystickSteering:
                    page = new ProfileSettingsMode1("ProfileSettingsMode1", profileCurrent, pipeClient, logger);
                    break;
            }
            mainWindow.NavView_Navigate(page);
        }

        private void cB_Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cB_Profiles.SelectedItem == null)
                return;

            profileCurrent = (Profile)cB_Profiles.SelectedItem;
            UpdateSelectedProfile();
        }

        private void UpdateSelectedProfile()
        {
            if (profileCurrent == null)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                // disable button if is default profile
                b_DeleteProfile.IsEnabled = !profileCurrent.isDefault;
                tB_ProfileName.IsEnabled = !profileCurrent.isDefault;

                GlobalSettings.IsEnabled = GlobalDetails.IsEnabled = profileCurrent.error != ProfileErrorCode.MissingPermission;
                b_ApplyProfile.IsEnabled = profileCurrent.error != ProfileErrorCode.MissingPermission;
                b_ApplyProfile.ToolTip = b_ApplyProfile.IsEnabled == false ? Properties.Resources.WarningElevated : null;

                // populate controls
                tB_ProfileName.Text = profileCurrent.name;
                tB_ProfilePath.Text = profileCurrent.fullpath;

                Toggle_EnableProfile.IsEnabled = !profileCurrent.isDefault;
                Toggle_EnableProfile.IsOn = profileCurrent.isEnabled;

                Toggle_UniversalMotion.IsOn = profileCurrent.umc_enabled;
                tb_ProfileGyroValue.Value = profileCurrent.gyrometer;
                tb_ProfileAcceleroValue.Value = profileCurrent.accelerometer;
                tb_ProfileAntiDeadzone.Value = profileCurrent.antideadzone;
                cB_GyroSteering.SelectedIndex = profileCurrent.steering;
                cB_InvertVertical.IsChecked = profileCurrent.invertvertical;
                cB_InvertHorizontal.IsChecked = profileCurrent.inverthorizontal;
                cB_Input.SelectedIndex = (int)profileCurrent.umc_input;
                cB_Output.SelectedIndex = (int)profileCurrent.umc_output;
                cB_Whitelist.IsChecked = profileCurrent.whitelisted;
                cB_Wrapper.IsChecked = profileCurrent.use_wrapper;

                foreach (GamepadButtonFlagsExt button in (GamepadButtonFlagsExt[])Enum.GetValues(typeof(GamepadButtonFlagsExt)))
                    if (profileCurrent.umc_trigger.HasFlag(button))
                        activators[button].IsChecked = true;
                    else
                        activators[button].IsChecked = false;

                // display warnings
                ProfileErrorCode currentError = profileCurrent.error;
                if (profileCurrent.isApplied)
                    currentError = ProfileErrorCode.IsRunning;

                switch (currentError)
                {
                    default:
                    case ProfileErrorCode.None:
                        WarningBorder.Visibility = Visibility.Collapsed;
                        cB_Whitelist.IsEnabled = true;
                        cB_Wrapper.IsEnabled = true;
                        break;

                    case ProfileErrorCode.MissingExecutable:
                    case ProfileErrorCode.MissingPath:
                    case ProfileErrorCode.MissingPermission:
                    case ProfileErrorCode.IsDefault:
                        WarningBorder.Visibility = Visibility.Visible;
                        WarningContent.Text = EnumUtils.GetDescriptionFromEnumValue(currentError);
                        cB_Whitelist.IsEnabled = false;     // you can't whitelist an application without path
                        cB_Wrapper.IsEnabled = false;       // you can't deploy wrapper on an application without path
                        break;

                    case ProfileErrorCode.IsRunning:
                        WarningBorder.Visibility = Visibility.Visible;
                        WarningContent.Text = EnumUtils.GetDescriptionFromEnumValue(currentError);
                        cB_Whitelist.IsEnabled = true; // you can't whitelist an application without path
                        cB_Wrapper.IsEnabled = false;   // you can't deploy wrapper on a running application
                        break;
                }
            });
        }

        private async void b_DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (profileCurrent == null)
                return;

            Task<ContentDialogResult> result = Dialog.ShowAsync($"{Properties.Resources.ProfilesPage_AreYouSureDelete1} \"{profileCurrent.name}\"?", 
                                                                $"{Properties.Resources.ProfilesPage_AreYouSureDelete2}", 
                                                                ContentDialogButton.Primary, 
                                                                $"{Properties.Resources.ProfilesPage_Cancel}", 
                                                                $"{Properties.Resources.ProfilesPage_Delete}");
            await result; // sync call

            switch (result.Result)
            {
                case ContentDialogResult.Primary:
                    profileManager.DeleteProfile(profileCurrent);
                    cB_Profiles.SelectedIndex = 0;
                    break;
                default:
                    break;
            }
        }

        private void b_ApplyProfile_Click(object sender, RoutedEventArgs e)
        {
            if (profileCurrent == null)
                return;
            
            Dialog.ShowAsync($"{Properties.Resources.ProfilesPage_ProfileUpdated1}",
                             $"{profileCurrent.name} {Properties.Resources.ProfilesPage_ProfileUpdated2}",
                             ContentDialogButton.Primary, null, $"{Properties.Resources.ProfilesPage_OK}");

            profileCurrent.name = tB_ProfileName.Text;
            profileCurrent.fullpath = tB_ProfilePath.Text;
            profileCurrent.isEnabled = (bool)Toggle_EnableProfile.IsOn;

            profileCurrent.gyrometer = (float)tb_ProfileGyroValue.Value;
            profileCurrent.accelerometer = (float)tb_ProfileAcceleroValue.Value;
            profileCurrent.antideadzone = (float)tb_ProfileAntiDeadzone.Value;
            profileCurrent.whitelisted = (bool)cB_Whitelist.IsChecked;
            profileCurrent.use_wrapper = (bool)cB_Wrapper.IsChecked;

            profileCurrent.steering = cB_GyroSteering.SelectedIndex;

            profileCurrent.invertvertical = (bool)cB_InvertVertical.IsChecked;
            profileCurrent.inverthorizontal = (bool)cB_InvertHorizontal.IsChecked;

            profileCurrent.umc_enabled = (bool)Toggle_UniversalMotion.IsOn;

            profileCurrent.umc_input = (Input)cB_Input.SelectedIndex;
            profileCurrent.umc_output = (Output)cB_Output.SelectedIndex;

            profileCurrent.umc_trigger = 0;

            foreach (GamepadButtonFlagsExt button in (GamepadButtonFlagsExt[])Enum.GetValues(typeof(GamepadButtonFlagsExt)))
                if ((bool)activators[button].IsChecked)
                    profileCurrent.umc_trigger |= button;

            profileManager.profiles[profileCurrent.name] = profileCurrent;
            profileManager.UpdateOrCreateProfile(profileCurrent, false);
            profileManager.SerializeProfile(profileCurrent);
        }

        private void cB_Whitelist_Checked(object sender, RoutedEventArgs e)
        {
            Expander_UMC.IsEnabled = (bool)!cB_Whitelist.IsChecked;
        }

        private void cB_Overlay_Checked(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void cB_Wrapper_Checked(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void cB_EnableHook_Checked(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void cB_ExclusiveHook_Checked(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void Toggle_UniversalMotion_Toggled(object sender, RoutedEventArgs e)
        {
            if (profileCurrent == null)
                return;

            cB_Whitelist.IsEnabled = !(bool)Toggle_UniversalMotion.IsOn && !profileCurrent.isDefault;
        }

        private void Scrolllock_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            MainWindow.scrollLock = true;
        }

        private void Scrolllock_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            MainWindow.scrollLock = false;
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            ((Expander)sender).BringIntoView();
        }

        private void Toggle_EnableProfile_Toggled(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void cB_Input_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cB_Input.SelectedIndex == -1)
                return;

            Input button = (Input)cB_Input.SelectedIndex;
            Text_InputHint.Text = Profile.InputDescription[button];
        }
    }
}
