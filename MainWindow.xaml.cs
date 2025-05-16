using System.Windows;
using System.Windows.Controls;
using taskmanager.Views;

namespace taskmanager
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            TabContentControl.Content = new ProcessesView();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            NavigationListBox.SelectedIndex = 0;
        }


        private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabContentControl == null)
                return;

            if (NavigationListBox.SelectedItem is ListBoxItem selectedItem)
            {
                if (selectedItem.Content is StackPanel stackPanel)
                {
                    foreach (var child in stackPanel.Children)
                    {
                        if (child is TextBlock textBlock)
                        {
                            string selectedTab = textBlock.Text;

                            switch (selectedTab)
                            {
                                case "Processes":
                                    TabContentControl.Content = new ProcessesView();
                                    break;
                                case "Performance":
                                    TabContentControl.Content = new PerformanceView();
                                    break;
                                case "Settings":
                                    TabContentControl.Content = new SettingsView();
                                    break;
                                default:
                                    TabContentControl.Content = new ProcessesView();
                                    break;
                            }
                            break;
                        }
                    }
                }
            }
        }


        private void MinimizeApp(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void MaximizeRestoreApp(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();

        }
    }
}