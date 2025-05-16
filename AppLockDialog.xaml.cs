using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace taskmanager
{
    public partial class AppLockDialog : Window
    {
        private const string APP_LOCK_FILE = "applock.config";
        private const string SETTINGS_FOLDER = "Settings";
        private Dictionary<string, string> _appLockConfig;

        public bool IsAuthenticated { get; private set; }

        public AppLockDialog()
        {
            InitializeComponent();
            LoadAppLockConfig();
            SetupSecurityQuestion();

            // Set focus to password box for better UX
            Loaded += (s, e) => PasswordBox.Focus();

            // Allow pressing Enter to login
            PasswordBox.KeyDown += (s, e) => {
                if (e.Key == System.Windows.Input.Key.Enter)
                    LoginButton_Click(s, e);
            };
        }

        private void LoadAppLockConfig()
        {
            _appLockConfig = new Dictionary<string, string>();
            string filePath = Path.Combine(SETTINGS_FOLDER, APP_LOCK_FILE);

            try
            {
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
                            _appLockConfig[key] = value;
                        }
                    }
                }
                else
                {
                    // If file doesn't exist, log and handle gracefully
                    Debug("App lock file not found");
                }
            }
            catch (Exception ex)
            {
                Debug($"Error loading app lock config: {ex.Message}");
                MessageBox.Show($"Error loading app lock: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetupSecurityQuestion()
        {
            if (_appLockConfig.ContainsKey("securityQuestion"))
            {
                SecurityQuestionValue.Text = _appLockConfig["securityQuestion"];
            }
            else
            {
                SecurityQuestionValue.Text = "No security question found.";
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_appLockConfig.ContainsKey("passwordHash"))
                {
                    string enteredPasswordHash = HashPassword(PasswordBox.Password);
                    string storedHash = _appLockConfig["passwordHash"];

                    Debug($"Comparing password hashes: {enteredPasswordHash.Substring(0, 8)}... == {storedHash.Substring(0, 8)}...");

                    if (enteredPasswordHash == storedHash)
                    {
                        Debug("Password match - authentication successful");
                        IsAuthenticated = true;
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        Debug("Password mismatch");
                        MessageBox.Show("Incorrect password. Please try again.",
                                        "Authentication Failed",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                        PasswordBox.Clear();
                        PasswordBox.Focus();
                    }
                }
                else
                {
                    Debug("No password hash found in config");
                    MessageBox.Show("App lock configuration error. Please contact support.",
                                    "Configuration Error",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug($"Login error: {ex.Message}");
                MessageBox.Show($"Authentication error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RecoverButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SecurityAnswerBox.Text))
                {
                    MessageBox.Show("Please enter an answer to the security question.",
                                   "Input Required",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                    SecurityAnswerBox.Focus();
                    return;
                }

                if (_appLockConfig.ContainsKey("securityAnswerHash"))
                {
                    string enteredAnswerHash = HashPassword(SecurityAnswerBox.Text.ToLower().Trim());
                    string storedAnswerHash = _appLockConfig["securityAnswerHash"];

                    Debug($"Comparing answer hashes: {enteredAnswerHash.Substring(0, 8)}... == {storedAnswerHash.Substring(0, 8)}...");

                    if (enteredAnswerHash == storedAnswerHash)
                    {
                        // Show the user their password recovery options
                        string newPassword = GenerateTemporaryPassword();
                        ResetPassword(newPassword);

                        Debug("Security answer correct, password reset");
                        MessageBox.Show($"Your password has been reset to: {newPassword}\n\n" +
                                        "Please change it from Settings after login.",
                                        "Password Reset",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);

                        // Fill in the password box so the user can just click login
                        PasswordBox.Password = newPassword;
                        PasswordBox.Focus();
                        SwitchToLoginTab();
                    }
                    else
                    {
                        Debug("Security answer mismatch");
                        MessageBox.Show("Incorrect answer. Please try again.",
                                        "Recovery Failed",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                        SecurityAnswerBox.Clear();
                        SecurityAnswerBox.Focus();
                    }
                }
                else
                {
                    Debug("No security answer hash found in config");
                    MessageBox.Show("Recovery information not found. Please contact support.",
                                    "Recovery Error",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug($"Recovery error: {ex.Message}");
                MessageBox.Show($"Recovery error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SwitchToLoginTab()
        {
            // Implement this to switch back to login tab in the UI
            if (MainTabControl != null)
                MainTabControl.SelectedIndex = 0;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Application.Current.Shutdown();
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            var stringChars = new char[8];
            var random = new Random();

            for (int i = 0; i < stringChars.Length; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            return new string(stringChars);
        }

        private void ResetPassword(string newPassword)
        {
            _appLockConfig["passwordHash"] = HashPassword(newPassword);
            _appLockConfig["resetDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Save updated config
            SaveAppLockConfig();
        }

        private void SaveAppLockConfig()
        {
            try
            {
                Directory.CreateDirectory(SETTINGS_FOLDER);
                string filePath = Path.Combine(SETTINGS_FOLDER, APP_LOCK_FILE);

                StringBuilder sb = new StringBuilder();
                foreach (var kvp in _appLockConfig)
                {
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");
                }

                File.WriteAllText(filePath, sb.ToString());
                Debug("App lock config saved successfully");
            }
            catch (Exception ex)
            {
                Debug($"Error saving app lock config: {ex.Message}");
                MessageBox.Show($"Error saving app lock settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Debug(string message)
        {
            // Implement proper logging here if needed
            Console.WriteLine($"[AppLockDialog] {message}");
        }
    }
}
