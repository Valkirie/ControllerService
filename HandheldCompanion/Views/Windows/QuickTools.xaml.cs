﻿using ControllerCommon;
using HandheldCompanion.Managers;
using HandheldCompanion.Views.QuickPages;
using ModernWpf.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.Windows
{
    /// <summary>
    /// Interaction logic for QuickTools.xaml
    /// </summary>
    public partial class QuickTools : Window
    {
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // page vars
        private Dictionary<string, Page> _pages = new();
        private string preNavItemTag;
        public QuickToolsPage4 quickPage4;

        // touchscroll vars
        Point scrollPoint = new Point();
        double scrollOffset = 1;
        public static bool scrollLock = false;

        public QuickTools()
        {
            InitializeComponent();

            // create pages
            quickPage4 = new QuickToolsPage4();
            _pages.Add("QuickToolsPage4", quickPage4);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            double TaskBarHeight = SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height;
            Height = SystemParameters.PrimaryScreenHeight - TaskBarHeight;
            Left = SystemParameters.PrimaryScreenWidth - Width;

            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
        }

        public void UpdateVisibility()
        {
            this.Dispatcher.Invoke(() =>
            {
                Visibility visibility = Visibility.Visible;
                switch (Visibility)
                {
                    case Visibility.Visible:
                        visibility = Visibility.Collapsed;
                        break;
                    case Visibility.Collapsed:
                    case Visibility.Hidden:
                        visibility = Visibility.Visible;
                        break;
                }
                Visibility = visibility;
            });
        }

        #region navView
        private void navView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer != null)
            {
                NavigationViewItem navItem = (NavigationViewItem)args.InvokedItemContainer;
                string navItemTag = (string)navItem.Tag;

                switch (navItemTag)
                {
                    default:
                        preNavItemTag = navItemTag;
                        break;
                }

                NavView_Navigate(preNavItemTag);
            }
        }

        public void NavView_Navigate(string navItemTag)
        {
            var item = _pages.FirstOrDefault(p => p.Key.Equals(navItemTag));
            Page _page = item.Value;

            // Get the page type before navigation so you can prevent duplicate
            // entries in the backstack.
            var preNavPageType = ContentFrame.CurrentSourcePageType;

            // Only navigate if the selected page isn't currently loaded.
            if (!(_page is null) && !Type.Equals(preNavPageType, _page))
            {
                NavView_Navigate(_page);
            }
        }

        public void NavView_Navigate(Page _page)
        {
            ContentFrame.Navigate(_page);
        }

        private void navView_Loaded(object sender, RoutedEventArgs e)
        {
            // Add handler for ContentFrame navigation.
            ContentFrame.Navigated += On_Navigated;

            // NavView doesn't load any page by default, so load home page.
            navView.SelectedItem = navView.MenuItems[0];

            // If navigation occurs on SelectionChanged, this isn't needed.
            // Because we use ItemInvoked to navigate, we need to call Navigate
            // here to load the home page.
            NavView_Navigate("QuickToolsPage4");
        }

        private void navView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            TryGoBack();
        }

        private bool TryGoBack()
        {
            if (!ContentFrame.CanGoBack)
                return false;

            // Don't go back if the nav pane is overlayed.
            if (navView.IsPaneOpen &&
                (navView.DisplayMode == NavigationViewDisplayMode.Compact ||
                 navView.DisplayMode == NavigationViewDisplayMode.Minimal))
                return false;

            ContentFrame.GoBack();
            return true;
        }

        private void On_Navigated(object sender, NavigationEventArgs e)
        {
            navView.IsBackEnabled = ContentFrame.CanGoBack;

            if (ContentFrame.SourcePageType != null)
            {
                var preNavPageType = ContentFrame.CurrentSourcePageType;
                var preNavPageName = preNavPageType.Name;

                var NavViewItem = navView.MenuItems
                    .OfType<NavigationViewItem>()
                    .Where(n => n.Tag.Equals(preNavPageName)).FirstOrDefault();

                if (!(NavViewItem is null))
                {
                    navView.SelectedItem = NavViewItem;
                    navView.Header = (string)NavViewItem.Content;
                }
                else
                {
                    navView.Header = ((Page)e.Content).Title;
                }
            }
        }
        #endregion

        #region scrollView
        private void ScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            scrollPoint = e.GetPosition(scrollViewer);
            scrollOffset = scrollViewer.VerticalOffset;
        }

        private bool hasScrolled;
        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (scrollPoint == new Point())
                return;

            if (scrollLock)
                return;

            double diff = (scrollPoint.Y - e.GetPosition(scrollViewer).Y);

            if (Math.Abs(diff) >= 3)
            {
                scrollViewer.ScrollToVerticalOffset(scrollOffset + diff);
                hasScrolled = true;
                e.Handled = true;
            }
            else
            {
                hasScrolled = false;
                e.Handled = false;
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            scrollPoint = new Point();

            if (hasScrolled)
            {
                e.Handled = true;
                hasScrolled = false;
            }
        }

        private void scrollViewer_MouseLeave(object sender, MouseEventArgs e)
        {
            scrollPoint = new Point();

            if (hasScrolled)
            {
                e.Handled = true;
                hasScrolled = false;
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
        }
        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = !isClosing;
            this.Visibility = Visibility.Collapsed;
        }

        private bool isClosing;
        public void Close(bool v)
        {
            isClosing = v;
            this.Close();
        }
    }
}
