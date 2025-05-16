using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Management;
using System.IO;

namespace taskmanager.Views
{
    public partial class ProcessesView : UserControl, INotifyPropertyChanged
    {
        // Collection initializations with modern syntax
        private ObservableCollection<ProcessItem> _processes = [];
        public ObservableCollection<ProcessItem> Processes
        {
            get => _processes;
            set
            {
                if (_processes != value)
                {
                    _processes = value;
                    OnPropertyChanged(nameof(Processes));
                }
            }
        }

        private readonly DispatcherTimer _performanceUpdateTimer = new();
        private readonly Dictionary<int, PerformanceCounter> _processCpuCounters = [];
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;
        private string _searchFilter = string.Empty;
        private bool _isLoading = false;
        private bool _autoUpdateEnabled = false; // Flag to control auto-updates
        private int _updateCounter = 0; // Add this as a class field

        // Field declarations with null-forgiving operator
        private TextBlock statusBar;
        private TextBlock processCountText;
        private Grid loadingOverlay;
        private DataGrid processesDataGrid;
        private TextBox searchBox;
        private Button endTaskButton;
        private Button propertiesButton;
        private CheckBox autoUpdateCheckBox; // New checkbox for enabling/disabling auto-updates

        public TextBlock GetStatusBar() => statusBar;
        private void SetStatusBar(TextBlock value) => statusBar = value;

        public TextBlock GetProcessCountText() => processCountText;
        private void SetProcessCountText(TextBlock value) => processCountText = value;

        public Grid GetLoadingOverlay() => loadingOverlay;
        private void SetLoadingOverlay(Grid value) => loadingOverlay = value;

        public DataGrid GetProcessesDataGrid() => processesDataGrid;
        private void SetProcessesDataGrid(DataGrid value) => processesDataGrid = value;

        public TextBox GetSearchBox() => searchBox;
        private void SetSearchBox(TextBox value) => searchBox = value;

        public Button GetEndTaskButton() => endTaskButton;
        private void SetEndTaskButton(Button value) => endTaskButton = value;

        public Button GetPropertiesButton() => propertiesButton;
        private void SetPropertiesButton(Button value) => propertiesButton = value;

        public CheckBox GetAutoUpdateCheckBox() => autoUpdateCheckBox;
        private void SetAutoUpdateCheckBox(CheckBox value) => autoUpdateCheckBox = value;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ProcessesView()
        {
            InitializeComponent();
            DataContext = this;

            // Initialize UI references if they're defined in XAML  
            SetStatusBar(FindName("StatusBar") as TextBlock ?? throw new InvalidOperationException("StatusBar not found"));
            SetProcessCountText(FindName("ProcessCountText") as TextBlock ?? throw new InvalidOperationException("ProcessCountText not found"));
            SetLoadingOverlay(FindName("LoadingOverlay") as Grid ?? throw new InvalidOperationException("LoadingOverlay not found"));
            SetProcessesDataGrid(FindName("ProcessesDataGrid") as DataGrid ?? throw new InvalidOperationException("ProcessesDataGrid not found"));
            SetSearchBox(FindName("SearchBox") as TextBox ?? throw new InvalidOperationException("SearchBox not found"));
            SetEndTaskButton(FindName("EndTaskButton") as Button ?? throw new InvalidOperationException("EndTaskButton not found"));
            SetPropertiesButton(FindName("PropertiesButton") as Button ?? throw new InvalidOperationException("PropertiesButton not found"));

            // Try to find auto-update checkbox if it exists in XAML
            try
            {
                SetAutoUpdateCheckBox(FindName("AutoUpdateCheckBox") as CheckBox);
                if (GetAutoUpdateCheckBox() != null)
                {
                    GetAutoUpdateCheckBox().Checked += AutoUpdateCheckBox_CheckedChanged;
                    GetAutoUpdateCheckBox().Unchecked += AutoUpdateCheckBox_CheckedChanged;
                }
            }
            catch
            {
                // Checkbox might not exist in XAML yet
            }

            GetProcessesDataGrid().ItemsSource = Processes;

            SetupPerformanceMonitoring();
            LoadProcessesAsync(); // Only load processes on initial load
        }

        public void SetStatus(string status)
        {
            StatusBar.Text = status;
            ReadyDot.Visibility = status == "Ready" ? Visibility.Visible : Visibility.Collapsed;
        }


        private void AutoUpdateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _autoUpdateEnabled = GetAutoUpdateCheckBox()?.IsChecked ?? false;

            if (_autoUpdateEnabled)
            {
                _performanceUpdateTimer.Start();
                GetStatusBar().Text = "Auto-update enabled";
            }
            else
            {
                _performanceUpdateTimer.Stop();
                GetStatusBar().Text = "Auto-update disabled";
            }
        }

        // Event handlers from XAML
        private async void EndProcessTreeButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            if (selectedProcess == null) return;

            try
            {
                int pid = int.Parse(selectedProcess.PID);
                await KillProcessAndChildrenAsync(pid);
                await LoadProcessesAsync(); // Reload processes after termination
                GetStatusBar().Text = $"Process tree for {selectedProcess.Name} (PID: {selectedProcess.PID}) terminated";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error terminating process tree: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task KillProcessAndChildrenAsync(int parentProcessId)
        {
            // Get all processes
            var processSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Process WHERE ParentProcessID=" + parentProcessId);
            var processCollection = processSearcher.Get();

            // Kill each child process
            foreach (ManagementObject mo in processCollection)
            {
                // Recursive call for each child process
                await KillProcessAndChildrenAsync(Convert.ToInt32(mo["ProcessID"]));
            }

            // Then kill the parent process
            try
            {
                Process proc = Process.GetProcessById(parentProcessId);
                proc.Kill();
                await Task.Delay(100); // Give it a moment to terminate
            }
            catch (Exception)
            {
                // Process may already be terminated
            }
        }

        private async void EndTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            if (selectedProcess == null) return;

            try
            {
                int pid = int.Parse(selectedProcess.PID);
                Process process = Process.GetProcessById(pid);
                process.Kill();
                await Task.Delay(100); // Give it a moment to terminate

                // Reload processes after termination
                await LoadProcessesAsync();
                GetStatusBar().Text = $"Process {selectedProcess.Name} (PID: {selectedProcess.PID}) terminated";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error terminating process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFileLocationButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            if (selectedProcess == null) return;

            try
            {
                int pid = int.Parse(selectedProcess.PID);
                Process process = Process.GetProcessById(pid);
                string filePath = process.MainModule?.FileName;

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    GetStatusBar().Text = $"Opened file location for {selectedProcess.Name}";
                }
                else
                {
                    MessageBox.Show("Cannot locate the file for this process.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            bool isProcessSelected = selectedProcess != null;

            GetEndTaskButton().IsEnabled = isProcessSelected;
            GetPropertiesButton().IsEnabled = isProcessSelected;

            if (isProcessSelected)
            {
                GetStatusBar().Text = $"Selected process: {selectedProcess.Name} (PID: {selectedProcess.PID})";

                // Update performance data just for the selected process
                if (!_autoUpdateEnabled)
                {
                    UpdateSelectedProcessPerformanceData(selectedProcess);
                }
            }
            else
            {
                GetStatusBar().Text = "Ready";
            }
        }

        private void UpdateSelectedProcessPerformanceData(ProcessItem processItem)
        {
            try
            {
                int pid = int.Parse(processItem.PID);
                Process process = Process.GetProcessById(pid);

                // Update Memory (which doesn't need a performance counter)
                long memoryBytes = process.WorkingSet64;
                double memoryMB = memoryBytes / (1024.0 * 1024.0);
                processItem.MemoryValue = memoryMB;
                processItem.Memory = FormatMemorySize(memoryBytes);

                // Update CPU only if we have a counter
                if (_processCpuCounters.TryGetValue(pid, out var cpuCounter))
                {
                    float cpuUsage = cpuCounter.NextValue();
                    processItem.CPUValue = cpuUsage;
                    processItem.CPU = $"{cpuUsage:F1}%";
                    processItem.CPUToolTip = $"CPU Usage: {cpuUsage:F1}%";
                }

                // Calculate memory percentage
                try
                {
                    ObjectQuery wql = new ObjectQuery("SELECT * FROM Win32_ComputerSystem");
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql);
                    ManagementObjectCollection results = searcher.Get();

                    double totalPhysicalMemory = 0;
                    foreach (ManagementObject result in results)
                    {
                        totalPhysicalMemory = Convert.ToDouble(result["TotalPhysicalMemory"]);
                        break;
                    }

                    processItem.MemoryPercentage = (memoryBytes / totalPhysicalMemory) * 100;
                    processItem.MemoryToolTip = $"Memory Usage: {processItem.Memory} ({processItem.MemoryPercentage:F1}% of total)";
                }
                catch
                {
                    processItem.MemoryPercentage = 0;
                    processItem.MemoryToolTip = $"Memory Usage: {processItem.Memory}";
                }
            }
            catch
            {
                // Process may have terminated
            }
        }

        private void PropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            if (selectedProcess == null) return;

            try
            {
                // Update the selected process data before showing properties
                UpdateSelectedProcessPerformanceData(selectedProcess);

                // Create and show a process properties dialog
                var properties = new Window
                {
                    Title = $"{selectedProcess.Name} Properties",
                    Width = 400,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var grid = new Grid();
                var stackPanel = new StackPanel { Margin = new Thickness(10) };

                stackPanel.Children.Add(new TextBlock { Text = $"Process Name: {selectedProcess.Name}", Margin = new Thickness(0, 5, 0, 5) });
                stackPanel.Children.Add(new TextBlock { Text = $"PID: {selectedProcess.PID}", Margin = new Thickness(0, 5, 0, 5) });
                stackPanel.Children.Add(new TextBlock { Text = $"CPU Usage: {selectedProcess.CPU}", Margin = new Thickness(0, 5, 0, 5) });
                stackPanel.Children.Add(new TextBlock { Text = $"Memory Usage: {selectedProcess.Memory}", Margin = new Thickness(0, 5, 0, 5) });
                stackPanel.Children.Add(new TextBlock { Text = $"Type: {selectedProcess.ProcessType}", Margin = new Thickness(0, 5, 0, 5) });
                stackPanel.Children.Add(new TextBlock { Text = $"Status: {selectedProcess.Status}", Margin = new Thickness(0, 5, 0, 5) });

                try
                {
                    int pid = int.Parse(selectedProcess.PID);
                    var process = Process.GetProcessById(pid);
                    var filePath = process.MainModule?.FileName ?? "N/A";
                    stackPanel.Children.Add(new TextBlock { Text = $"File Path: {filePath}", Margin = new Thickness(0, 5, 0, 5), TextWrapping = TextWrapping.Wrap });

                    var startTime = process.StartTime.ToString("g");
                    stackPanel.Children.Add(new TextBlock { Text = $"Start Time: {startTime}", Margin = new Thickness(0, 5, 0, 5) });

                    TimeSpan runTime = DateTime.Now - process.StartTime;
                    stackPanel.Children.Add(new TextBlock { Text = $"Running Time: {runTime:hh\\:mm\\:ss}", Margin = new Thickness(0, 5, 0, 5) });
                }
                catch
                {
                    stackPanel.Children.Add(new TextBlock { Text = "Additional information unavailable", Margin = new Thickness(0, 5, 0, 5) });
                }

                var closeButton = new Button
                {
                    Content = "Close",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(20, 5, 20, 5),
                    Margin = new Thickness(0, 20, 0, 0)
                };
                closeButton.Click += (s, args) => properties.Close();
                stackPanel.Children.Add(closeButton);

                grid.Children.Add(stackPanel);
                properties.Content = grid;
                properties.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying properties: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProcessesAsync();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchFilter = GetSearchBox().Text.Trim().ToLower();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                // Show all processes
                GetProcessesDataGrid().ItemsSource = Processes;
                GetProcessCountText().Text = $"Processes: {Processes.Count}";
            }
            else
            {
                // Filter processes based on search text
                var filteredProcesses = Processes.Where(p =>
                    p.Name.ToLower().Contains(_searchFilter) ||
                    p.PID.Contains(_searchFilter) ||
                    p.ProcessType.ToLower().Contains(_searchFilter)
                ).ToList();

                GetProcessesDataGrid().ItemsSource = filteredProcesses;
                GetProcessCountText().Text = $"Processes: {filteredProcesses.Count} (filtered from {Processes.Count})";
            }
        }

        private void SetupPerformanceMonitoring()
        {
            try
            {
                // Set up CPU and RAM counters
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

                // Set up timer for regular updates but don't start it automatically
                _performanceUpdateTimer.Interval = TimeSpan.FromSeconds(4); // Increased interval to reduce resource usage
                _performanceUpdateTimer.Tick += async (s, e) => await UpdatePerformanceDataAsync();

                // Only start the timer if auto-update is enabled
                if (_autoUpdateEnabled)
                {
                    _performanceUpdateTimer.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error setting up performance monitoring: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdatePerformanceDataAsync()
        {
            if (_isLoading || !_autoUpdateEnabled) return;

            try
            {
                // Limit updates to visible and selected processes to improve performance
                var dataGrid = GetProcessesDataGrid();

                // Determine which processes are actually visible in the UI
                var visibleProcessItems = new List<ProcessItem>();

                if (dataGrid.ItemsSource is IEnumerable<ProcessItem> items)
                {
                    foreach (var item in items)
                    {
                        // If there's a way to determine visibility, use it here
                        // For now, we'll update all items in the current view
                        visibleProcessItems.Add(item);

                        // Optimization: Limit to max 50 visible items to avoid performance issues
                        if (visibleProcessItems.Count >= 50)
                            break;
                    }
                }

                // Add the selected item if it exists and isn't already in the list
                var selectedItem = dataGrid.SelectedItem as ProcessItem;
                if (selectedItem != null && !visibleProcessItems.Contains(selectedItem))
                {
                    visibleProcessItems.Add(selectedItem);
                }

                // Update only visible processes
                foreach (var processItem in visibleProcessItems)
                {
                    try
                    {
                        int pid = int.Parse(processItem.PID);
                        Process process = Process.GetProcessById(pid);

                        // Update CPU
                        if (_processCpuCounters.TryGetValue(pid, out var cpuCounter))
                        {
                            float cpuUsage = cpuCounter.NextValue();
                            processItem.CPUValue = cpuUsage;
                            processItem.CPU = $"{cpuUsage:F1}%";
                            processItem.CPUToolTip = $"CPU Usage: {cpuUsage:F1}%";
                        }

                        // Update Memory
                        long memoryBytes = process.WorkingSet64;
                        double memoryMB = memoryBytes / (1024.0 * 1024.0);
                        processItem.MemoryValue = memoryMB;
                        processItem.Memory = FormatMemorySize(memoryBytes);

                        // We won't calculate memory percentage for all processes during auto-update
                        // to reduce performance impact
                    }
                    catch
                    {
                        // Process may have terminated
                    }
                }

                // Check for processes that no longer exist
                bool processesChanged = false;
                var processesToRemove = new List<ProcessItem>();

                foreach (var process in Processes)
                {
                    try
                    {
                        int pid = int.Parse(process.PID);
                        Process.GetProcessById(pid); // Just checking if it throws
                    }
                    catch
                    {
                        // Process no longer exists
                        processesToRemove.Add(process);
                        processesChanged = true;
                    }
                }

                // Remove terminated processes
                if (processesChanged)
                {
                    foreach (var process in processesToRemove)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Processes.Remove(process);
                        });
                    }

                    // Update process count
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        GetProcessCountText().Text = $"Processes: {Processes.Count}";
                        ApplyFilter(); // Reapply filter if needed
                    });
                }

                _updateCounter++;

                if (_updateCounter >= 5)
                {
                    _updateCounter = 0;
                    await CheckForNewProcessesAsync();
                }
            }
            catch (Exception ex)
            {
                GetStatusBar().Text = $"Error updating performance data: {ex.Message}";
            }
        }

        private async Task CheckForNewProcessesAsync()
        {
            try
            {
                // Get all current processes
                var allProcesses = Process.GetProcesses();
                var existingPids = Processes.Select(p => int.Parse(p.PID)).ToHashSet();
                bool processesAdded = false;

                foreach (var process in allProcesses)
                {
                    if (process != null && !existingPids.Contains(process.Id))
                    {
                        // New process found
                        try
                        {
                            // Create new CPU counter for this process
                            PerformanceCounter cpuCounter = null;
                            try
                            {
                                cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName);
                                cpuCounter.NextValue(); // First call always returns 0
                                _processCpuCounters[process.Id] = cpuCounter;
                            }
                            catch
                            {
                                // If counter creation fails, continue without performance data
                            }

                            string processType = DetermineProcessType(process);
                            string status = process.Responding ? "Running" : "Not responding";

                            // Determine process priority
                            string priority = "Normal";
                            try
                            {
                                priority = process.PriorityClass switch
                                {
                                    ProcessPriorityClass.RealTime => "Realtime",
                                    ProcessPriorityClass.High => "High",
                                    ProcessPriorityClass.AboveNormal => "Above Normal",
                                    ProcessPriorityClass.Normal => "Normal",
                                    ProcessPriorityClass.BelowNormal => "Below Normal",
                                    ProcessPriorityClass.Idle => "Low",
                                    _ => "Normal"
                                };
                            }
                            catch
                            {
                                // Use default priority if we can't access it
                            }

                            var processItem = new ProcessItem
                            {
                                Name = process.ProcessName,
                                PID = process.Id.ToString(),
                                CPU = "0.0%",
                                CPUValue = 0,
                                CPUToolTip = "CPU Usage: 0.0%",
                                Memory = FormatMemorySize(process.WorkingSet64),
                                MemoryValue = process.WorkingSet64 / (1024.0 * 1024.0),
                                DiskUsage = "0 KB/s",
                                NetworkUsage = "0 KB/s",
                                ProcessType = processType,
                                Status = status,
                                Priority = priority
                            };

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Processes.Add(processItem);
                                processesAdded = true;
                            });
                        }
                        catch
                        {
                            // Skip this process if we encounter errors
                        }
                    }
                }

                if (processesAdded)
                {
                    // Update process count
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        GetProcessCountText().Text = $"Processes: {Processes.Count}";
                        ApplyFilter(); // Reapply filter if needed
                    });
                }
            }
            catch (Exception ex)
            {
                GetStatusBar().Text = $"Error checking for new processes: {ex.Message}";
            }
        }

        private async Task LoadProcessesAsync()
        {
            if (_isLoading) return;

            _isLoading = true;
            GetLoadingOverlay().Visibility = Visibility.Visible;
            GetStatusBar().Text = "Loading processes...";
            SetStatus("Loading processes...");
            try
            {
                await Task.Run(() => {
                    // Create a new collection to avoid UI thread issues
                    var newProcesses = new ObservableCollection<ProcessItem>();
                    var allProcesses = Process.GetProcesses();

                    // Clean up existing CPU counters
                    foreach (var counter in _processCpuCounters.Values)
                    {
                        try
                        {
                            counter?.Dispose();
                        }
                        catch { /* Ignore disposal errors */ }
                    }
                    _processCpuCounters.Clear();

                    foreach (var process in allProcesses)
                    {
                        try
                        {
                            // Skip process if null (shouldn't happen but being defensive)
                            if (process == null) continue;

                            // Create new CPU counter for this process
                            PerformanceCounter cpuCounter = null;
                            try
                            {
                                cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName);
                                cpuCounter.NextValue(); // First call always returns 0
                                _processCpuCounters[process.Id] = cpuCounter;
                            }
                            catch
                            {
                                // If counter creation fails, continue without performance data
                            }

                            string processType = DetermineProcessType(process);
                            string status = process.Responding ? "Running" : "Not responding";

                            // Determine process priority
                            string priority = "Normal";
                            try
                            {
                                priority = process.PriorityClass switch
                                {
                                    ProcessPriorityClass.RealTime => "Realtime",
                                    ProcessPriorityClass.High => "High",
                                    ProcessPriorityClass.AboveNormal => "Above Normal",
                                    ProcessPriorityClass.Normal => "Normal",
                                    ProcessPriorityClass.BelowNormal => "Below Normal",
                                    ProcessPriorityClass.Idle => "Low",
                                    _ => "Normal"
                                };
                            }
                            catch
                            {
                                // Use default priority if we can't access it
                            }

                            var processItem = new ProcessItem
                            {
                                Name = process.ProcessName,
                                PID = process.Id.ToString(),
                                CPU = "0.0%",
                                CPUValue = 0,
                                CPUToolTip = "CPU Usage: 0.0%",
                                Memory = FormatMemorySize(process.WorkingSet64),
                                MemoryValue = process.WorkingSet64 / (1024.0 * 1024.0),
                                DiskUsage = "0 KB/s",
                                NetworkUsage = "0 KB/s",
                                ProcessType = processType,
                                Status = status,
                                Priority = priority
                            };

                            Application.Current.Dispatcher.Invoke(() => {
                                newProcesses.Add(processItem);
                            });
                        }
                        catch
                        {
                            // Skip this process if we encounter errors
                        }
                    }

                    // Update the UI on the main thread
                    Application.Current.Dispatcher.Invoke(() => {
                        Processes = newProcesses;
                        var dataGrid = GetProcessesDataGrid();
                        if (dataGrid != null)
                        {
                            dataGrid.ItemsSource = Processes;
                        }

                        var countText = GetProcessCountText();
                        if (countText != null)
                        {
                            countText.Text = $"Processes: {Processes.Count}";
                        }

                        ApplyFilter(); // Apply any existing filter
                    });
                });

                // Only update performance data if auto-update is enabled
                if (_autoUpdateEnabled)
                {
                    await UpdatePerformanceDataAsync();
                }

                GetStatusBar().Text = "Processes loaded successfully";
                SetStatus("Ready");
            }
            catch (Exception ex)
            {
                SetStatus("Error");
                GetStatusBar().Text = $"Error loading processes: {ex.Message}";
                MessageBox.Show($"Error loading processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                GetLoadingOverlay().Visibility = Visibility.Collapsed;
            }
        }


        private string DetermineProcessType(Process process)
        {
            try
            {
                if (process.SessionId == 0)
                    return "System";

                if (process.MainWindowHandle != IntPtr.Zero)
                    return "App";

                return "Background";
            }
            catch
            {
                return "Unknown";
            }
        }

        private void SetPriorityRealtime_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            ChangeProcessPriority(selectedProcess, ProcessPriorityClass.RealTime);
        }

        private void SetPriorityHigh_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            ChangeProcessPriority(selectedProcess, ProcessPriorityClass.High);
        }

        private void SetPriorityAboveNormal_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            ChangeProcessPriority(selectedProcess, ProcessPriorityClass.AboveNormal);
        }

        private void SetPriorityNormal_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            ChangeProcessPriority(selectedProcess, ProcessPriorityClass.Normal);
        }

        private void SetPriorityBelowNormal_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            ChangeProcessPriority(selectedProcess, ProcessPriorityClass.BelowNormal);
        }

        private void SetPriorityIdle_Click(object sender, RoutedEventArgs e)
        {
            var selectedProcess = GetProcessesDataGrid().SelectedItem as ProcessItem;
            ChangeProcessPriority(selectedProcess, ProcessPriorityClass.Idle);
        }

        private void ChangeProcessPriority(ProcessItem processItem, ProcessPriorityClass priorityClass)
        {
            if (processItem == null) return;

            try
            {
                int pid = int.Parse(processItem.PID);
                var process = Process.GetProcessById(pid);

                // Special handling for Realtime priority which requires admin rights
                if (priorityClass == ProcessPriorityClass.RealTime)
                {
                    var result = MessageBox.Show(
                        "Setting a process to Realtime priority may affect system stability and requires administrator privileges. Are you sure you want to continue?",
                        "Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                // Set the new priority
                process.PriorityClass = priorityClass;

                // Update the process item with the new priority
                processItem.Priority = priorityClass switch
                {
                    ProcessPriorityClass.RealTime => "Realtime",
                    ProcessPriorityClass.High => "High",
                    ProcessPriorityClass.AboveNormal => "Above Normal",
                    ProcessPriorityClass.Normal => "Normal",
                    ProcessPriorityClass.BelowNormal => "Below Normal",
                    ProcessPriorityClass.Idle => "Low",
                    _ => "Normal"
                };

                // Update the status bar with feedback
                GetStatusBar().Text = $"Changed priority of {processItem.Name} (PID: {processItem.PID}) to {processItem.Priority}";
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException || ex.Message.Contains("Access is denied"))
                {
                    MessageBox.Show(
                        "You don't have sufficient permissions to change this process priority. Try running the application as Administrator.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"Error changing process priority: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }



        private string FormatMemorySize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double size = bytes;

            while (size >= 1024 && counter < suffixes.Length - 1)
            {
                size /= 1024;
                counter++;
            }

            return $"{size:F1} {suffixes[counter]}";
        }
    }

    public class ProcessItem : INotifyPropertyChanged
    {
        private string _priority = "Normal";
        public string Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    OnPropertyChanged(nameof(Priority));
                }
            }
        }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        private string _pid = string.Empty;
        public string PID
        {
            get => _pid;
            set
            {
                if (_pid != value)
                {
                    _pid = value;
                    OnPropertyChanged(nameof(PID));
                }
            }
        }

        private string _cpu = string.Empty;
        public string CPU
        {
            get => _cpu;
            set
            {
                if (_cpu != value)
                {
                    _cpu = value;
                    OnPropertyChanged(nameof(CPU));
                }
            }
        }

        private float _cpuValue;
        public float CPUValue
        {
            get => _cpuValue;
            set
            {
                if (_cpuValue != value)
                {
                    _cpuValue = value;
                    OnPropertyChanged(nameof(CPUValue));
                }
            }
        }

        private string _cpuToolTip = string.Empty;
        public string CPUToolTip
        {
            get => _cpuToolTip;
            set
            {
                if (_cpuToolTip != value)
                {
                    _cpuToolTip = value;
                    OnPropertyChanged(nameof(CPUToolTip));
                }
            }
        }

        private string _memory = string.Empty;
        public string Memory
        {
            get => _memory;
            set
            {
                if (_memory != value)
                {
                    _memory = value;
                    OnPropertyChanged(nameof(Memory));
                }
            }
        }

        private double _memoryValue;
        public double MemoryValue
        {
            get => _memoryValue;
            set
            {
                if (_memoryValue != value)
                {
                    _memoryValue = value;
                    OnPropertyChanged(nameof(MemoryValue));
                }
            }
        }

        private double _memoryPercentage;
        public double MemoryPercentage
        {
            get => _memoryPercentage;
            set
            {
                if (_memoryPercentage != value)
                {
                    _memoryPercentage = value;
                    OnPropertyChanged(nameof(MemoryPercentage));
                }
            }
        }

        private string _memoryToolTip = string.Empty;
        public string MemoryToolTip
        {
            get => _memoryToolTip;
            set
            {
                if (_memoryToolTip != value)
                {
                    _memoryToolTip = value;
                    OnPropertyChanged(nameof(MemoryToolTip));
                }
            }
        }

        private string _diskUsage = string.Empty;
        public string DiskUsage
        {
            get => _diskUsage;
            set
            {
                if (_diskUsage != value)
                {
                    _diskUsage = value;
                    OnPropertyChanged(nameof(DiskUsage));
                }
            }
        }

        private string _networkUsage = string.Empty;
        public string NetworkUsage
        {
            get => _networkUsage;
            set
            {
                if (_networkUsage != value)
                {
                    _networkUsage = value;
                    OnPropertyChanged(nameof(NetworkUsage));
                }
            }
        }

        private string _processType = string.Empty;
        public string ProcessType
        {
            get => _processType;
            set
            {
                if (_processType != value)
                {
                    _processType = value;
                    OnPropertyChanged(nameof(ProcessType));
                }
            }
        }

        private string _status = string.Empty;
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}