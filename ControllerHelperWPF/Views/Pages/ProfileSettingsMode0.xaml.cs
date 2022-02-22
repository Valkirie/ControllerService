﻿using ControllerCommon;
using System.Windows;
using System.Windows.Controls;

namespace ControllerHelperWPF.Views.Pages
{
    /// <summary>
    /// Interaction logic for ProfileSettingsMode0.xaml
    /// </summary>
    public partial class ProfileSettingsMode0 : Page
    {
        private Profile profileCurrent;

        public ProfileSettingsMode0()
        {
            InitializeComponent();
        }

        public ProfileSettingsMode0(Profile profileCurrent) : this()
        {
            this.profileCurrent = profileCurrent;

            SliderSensivity.Value = profileCurrent.umc_sensivity;
            SliderIntensity.Value = profileCurrent.umc_intensity;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void SliderSensivity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (profileCurrent is null)
                return;

            profileCurrent.umc_sensivity = (float)SliderSensivity.Value;
        }

        private void SliderIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (profileCurrent is null)
                return;

            profileCurrent.umc_intensity = (float)SliderIntensity.Value;
        }
    }
}
