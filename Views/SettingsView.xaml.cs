using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.IO;

namespace taskmanager.Views
{
    public partial class SettingsView : UserControl
    {
        private const string APP_LOCK_FILE = "applock.config";
        private const string SETTINGS_FOLDER = "Settings";

        public SettingsView()
        {
            InitializeComponent();
            LoadSystemInformation();
            LoadAppLockSettings();
            LoadThemeSettings();
        }

        private void LoadSystemInformation()
        {
            try
            {
                // Load user information
                UsernameValue.Text = Environment.UserName;
                ComputerNameValue.Text = Environment.MachineName;
                OSValue.Text = Environment.OSVersion.VersionString;

                // Load CPU information
                string cpuInfo = "Unknown";
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            cpuInfo = obj["Name"].ToString();
                            break;
                        }
                    }
                }
                catch { }
                CPUValue.Text = cpuInfo;

                // Load memory information
                string memoryInfo = "Unknown";
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            ulong totalMemoryKB = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                            memoryInfo = $"{totalMemoryKB / (1024 * 1024 * 1024):N1} GB RAM";
                            break;
                        }
                    }
                }
                catch { }
                MemoryValue.Text = memoryInfo;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading system information: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAppLockSettings()
        {
            try
            {
                // Create settings directory if it doesn't exist
                Directory.CreateDirectory(SETTINGS_FOLDER);

                // Check if app lock is enabled
                bool appLockEnabled = File.Exists(Path.Combine(SETTINGS_FOLDER, APP_LOCK_FILE));
                EnableAppLockCheckbox.IsChecked = appLockEnabled;

                // If app lock is enabled, we don't load the password for security reasons
                // But we can show that a password is set
                if (appLockEnabled)
                {
                    var config = ReadAppLockConfig();
                    if (config.ContainsKey("securityQuestion"))
                    {
                        string question = config["securityQuestion"];
                        foreach (ComboBoxItem item in SecurityQuestionComboBox.Items)
                        {
                            if (item.Content.ToString() == question)
                            {
                                SecurityQuestionComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                    // Don't load the security answer for UI, just leave it blank
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading app lock settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadThemeSettings()
        {
            // Load theme settings (this would connect to your app's theme system)
            // Placeholder implementation
            ThemeComboBox.SelectedIndex = 0; // Default to Light Theme
            StartMinimizedCheckBox.IsChecked = false;
        }

        private void EnableAppLockCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            AppLockSettingsPanel.IsEnabled = true;
            AppLockSettingsPanel.Visibility = Visibility.Visible;
        }

        private void EnableAppLockCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            AppLockSettingsPanel.IsEnabled = false;
            AppLockSettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void SaveAppLockSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (EnableAppLockCheckbox.IsChecked == true)
                {
                    // Validate inputs
                    if (string.IsNullOrEmpty(AppLockPasswordBox.Password))
                    {
                        MessageBox.Show("Please enter a password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        AppLockPasswordBox.Focus();
                        return;
                    }

                    if (AppLockPasswordBox.Password != ConfirmPasswordBox.Password)
                    {
                        MessageBox.Show("Passwords do not match.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ConfirmPasswordBox.Clear();
                        ConfirmPasswordBox.Focus();
                        return;
                    }

                    if (SecurityQuestionComboBox.SelectedItem == null)
                    {
                        MessageBox.Show("Please select a security question.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        SecurityQuestionComboBox.Focus();
                        return;
                    }

                    if (string.IsNullOrEmpty(SecurityAnswerTextBox.Text))
                    {
                        MessageBox.Show("Please provide an answer to your security question.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        SecurityAnswerTextBox.Focus();
                        return;
                    }

                    // Hash the password and security answer
                    string passwordHash = HashPassword(AppLockPasswordBox.Password);
                    string securityQuestion = ((ComboBoxItem)SecurityQuestionComboBox.SelectedItem).Content.ToString();
                    string securityAnswerHash = HashPassword(SecurityAnswerTextBox.Text.ToLower().Trim());

                    // Check for existing config first
                    var existingConfig = ReadAppLockConfig();

                    // Create the config object - preserve any existing values we don't want to overwrite
                    var config = new Dictionary<string, string>();

                    // Add existing values (if any) that we want to preserve
                    foreach (var kvp in existingConfig)
                    {
                        // Skip fields we're explicitly updating
                        if (kvp.Key != "passwordHash" &&
                            kvp.Key != "securityQuestion" &&
                            kvp.Key != "securityAnswerHash")
                        {
                            config[kvp.Key] = kvp.Value;
                        }
                    }

                    // Add our updated values
                    config["passwordHash"] = passwordHash;
                    config["securityQuestion"] = securityQuestion;
                    config["securityAnswerHash"] = securityAnswerHash;

                    // Add metadata
                    if (!existingConfig.ContainsKey("created"))
                    {
                        config["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    config["updated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    // Save the config to a file
                    SaveAppLockConfig(config);

                    // Provide feedback to the user
                    MessageBox.Show("App lock settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Clear sensitive information from UI
                    AppLockPasswordBox.Clear();
                    ConfirmPasswordBox.Clear();
                    SecurityAnswerTextBox.Clear();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving app lock settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveAppLock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string lockFilePath = Path.Combine(SETTINGS_FOLDER, APP_LOCK_FILE);
                if (File.Exists(lockFilePath))
                {
                    // Ask for confirmation
                    MessageBoxResult result = MessageBox.Show(
                        "Are you sure you want to remove the app lock? This will disable password protection.",
                        "Confirm",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        File.Delete(lockFilePath);
                        EnableAppLockCheckbox.IsChecked = false;
                        MessageBox.Show("App lock removed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing app lock: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This would connect to your app's theme system
            // For now, just a placeholder
            string selectedTheme = ((ComboBoxItem)ThemeComboBox.SelectedItem).Content.ToString();
            // Apply theme changes here
        }

        #region App Lock Helper Methods

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private void SaveAppLockConfig(Dictionary<string, string> config)
        {
            Directory.CreateDirectory(SETTINGS_FOLDER);
            string filePath = Path.Combine(SETTINGS_FOLDER, APP_LOCK_FILE);

            StringBuilder sb = new StringBuilder();
            foreach (var kvp in config)
            {
                sb.AppendLine($"{kvp.Key}={kvp.Value}");
            }

            File.WriteAllText(filePath, sb.ToString());
        }

        private Dictionary<string, string> ReadAppLockConfig()
        {
            Dictionary<string, string> config = new Dictionary<string, string>();
            string filePath = Path.Combine(SETTINGS_FOLDER, APP_LOCK_FILE);

            if (File.Exists(filePath))
            {
                string[] lines = File.ReadAllLines(filePath);
                foreach (string line in lines)
                {
                    int index = line.IndexOf('=');
                    if (index > 0)
                    {
                        string key = line.Substring(0, index);
                        string value = line.Substring(index + 1);
                        config[key] = value;
                    }
                }
            }

            return config;
        }

        #endregion
    }
}
