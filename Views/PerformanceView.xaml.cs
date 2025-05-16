using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCharts;
using LiveCharts.Wpf;
using System.Net.NetworkInformation;

namespace taskmanager.Views
{
    public partial class PerformanceView : UserControl, INotifyPropertyChanged
    {
        private const int MAX_POINTS = 60; // 2 minutes of data at 2-second intervals

        private DispatcherTimer _performanceUpdateTimer;
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;
        private PerformanceCounter _diskReadCounter;
        private PerformanceCounter _diskWriteCounter;
        private PerformanceCounter _networkSentCounter;
        private PerformanceCounter _networkReceivedCounter;

        private string _selectedResource = "CPU"; // Default selected resource

        // LiveCharts collections
        private ChartValues<double> _cpuValues = new ChartValues<double>();
        private ChartValues<double> _ramValues = new ChartValues<double>();
        private ChartValues<double> _diskValues = new ChartValues<double>();
        private ChartValues<double> _networkValues = new ChartValues<double>();
        private ChartValues<double> _gpuValues = new ChartValues<double>();
        private ChartValues<double> _batteryValues = new ChartValues<double>();

        public SeriesCollection CurrentSeries { get; set; }
        public Func<double, string> YFormatter { get; set; }
        public string[] Labels { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public PerformanceView()
        {
            InitializeComponent();
            DataContext = this;

            // Initialize chart values
            InitializeChartValues();

            // Set initial series for CPU
            SetupChartForResource("CPU");

            // Initialize performance counters
            InitializePerformanceCounters();

            // Set up the timer for performance data collection
            SetupPerformanceMonitoring();

            // Update system info at the top of the window
            UpdateSystemInfo();
        }

        private void InitializeChartValues()
        {
            // Pre-populate with empty values
            for (int i = 0; i < MAX_POINTS; i++)
            {
                _cpuValues.Add(0);
                _ramValues.Add(0);
                _diskValues.Add(0);
                _networkValues.Add(0);
                _gpuValues.Add(0);
                _batteryValues.Add(0);
            }

            // Configure y-axis formatter
            YFormatter = value => value.ToString("F1") + "%";

            // Empty labels for x-axis (only show values)
            Labels = new string[MAX_POINTS];
            for (int i = 0; i < MAX_POINTS; i++)
            {
                Labels[i] = "";
            }
        }

        private void SetupChartForResource(string resourceType)
        {
            switch (resourceType)
            {
                case "CPU":
                    CurrentSeries = new SeriesCollection
                    {
                        new LineSeries
                        {
                            Title = "CPU Usage",
                            Values = _cpuValues,
                            PointGeometry = null,
                            StrokeThickness = 2,
                            Stroke = new SolidColorBrush(Colors.DodgerBlue),
                            Fill = new SolidColorBrush(Color.FromArgb(50, 30, 144, 255))
                        }
                    };
                    break;

                case "Memory":
                    CurrentSeries = new SeriesCollection
                    {
                        new LineSeries
                        {
                            Title = "Memory Usage",
                            Values = _ramValues,
                            PointGeometry = null,
                            StrokeThickness = 2,
                            Stroke = new SolidColorBrush(Colors.MediumSeaGreen),
                            Fill = new SolidColorBrush(Color.FromArgb(50, 60, 179, 113))
                        }
                    };
                    break;

                case "Disk":
                    CurrentSeries = new SeriesCollection
                    {
                        new LineSeries
                        {
                            Title = "Disk Activity",
                            Values = _diskValues,
                            PointGeometry = null,
                            StrokeThickness = 2,
                            Stroke = new SolidColorBrush(Colors.Coral),
                            Fill = new SolidColorBrush(Color.FromArgb(50, 255, 127, 80))
                        }
                    };
                    break;

                case "Network":
                    CurrentSeries = new SeriesCollection
                    {
                        new LineSeries
                        {
                            Title = "Network Activity",
                            Values = _networkValues,
                            PointGeometry = null,
                            StrokeThickness = 2,
                            Stroke = new SolidColorBrush(Colors.Purple),
                            Fill = new SolidColorBrush(Color.FromArgb(50, 128, 0, 128))
                        }
                    };
                    break;

                case "GPU":
                    CurrentSeries = new SeriesCollection
                    {
                        new LineSeries
                        {
                            Title = "GPU Usage",
                            Values = _gpuValues,
                            PointGeometry = null,
                            StrokeThickness = 2,
                            Stroke = new SolidColorBrush(Colors.Orange),
                            Fill = new SolidColorBrush(Color.FromArgb(50, 255, 165, 0))
                        }
                    };
                    break;

                case "Battery":
                    CurrentSeries = new SeriesCollection
                    {
                        new LineSeries
                        {
                            Title = "Battery Remaining",
                            Values = _batteryValues,
                            PointGeometry = null,
                            StrokeThickness = 2,
                            Stroke = new SolidColorBrush(Colors.Gold),
                            Fill = new SolidColorBrush(Color.FromArgb(50, 255, 215, 0))
                        }
                    };
                    break;
            }

            // Configure chart axes
            if (PerformanceChart != null)
            {
                // Update chart properties
                PerformanceChart.Series = CurrentSeries;
                PerformanceChart.AxisY[0].LabelFormatter = YFormatter;

                // Configure axes explicitly
                PerformanceChart.AxisY[0].MinValue = 0;
                PerformanceChart.AxisY[0].MaxValue = 100;
                PerformanceChart.AxisY[0].Title = resourceType + " %";

                // No x-axis labels needed for real-time charts
                PerformanceChart.AxisX[0].Labels = Labels;
                PerformanceChart.AxisX[0].ShowLabels = false;
            }

            OnPropertyChanged(nameof(CurrentSeries));
        }

        private void InitializePerformanceCounters()
        {
            try
            {
                // Initialize basic performance counters
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

                try { _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total"); } catch { }
                try { _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total"); } catch { }

                try
                {
                    _networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", GetFirstNetworkInterface());
                    _networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", GetFirstNetworkInterface());
                }
                catch { }

                // Take initial readings to initialize the counters
                _cpuCounter?.NextValue();
                _ramCounter?.NextValue();
                _diskReadCounter?.NextValue();
                _diskWriteCounter?.NextValue();
                _networkSentCounter?.NextValue();
                _networkReceivedCounter?.NextValue();
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Error initializing performance counters: {ex.Message}";
            }
        }

        private string GetFirstNetworkInterface()
        {
            // Get the first active network interface instance name  
            var category = new PerformanceCounterCategory("Network Interface");
            string[] instances = category.GetInstanceNames();
            return instances.Length > 0 ? instances[0] : "";
        }

        private void SetupPerformanceMonitoring()
        {
            _performanceUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _performanceUpdateTimer.Tick += PerformanceUpdateTimer_Tick;
            _performanceUpdateTimer.Start();
        }

        private void PerformanceUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Collect performance data
                float cpuUsage = _cpuCounter?.NextValue() ?? 0;
                float ramUsage = _ramCounter?.NextValue() ?? 0;

                float diskReadBytes = _diskReadCounter?.NextValue() ?? 0;
                float diskWriteBytes = _diskWriteCounter?.NextValue() ?? 0;
                float diskActivity = Math.Min(100, (diskReadBytes + diskWriteBytes) / (1024 * 1024) * 5); // Scale for visibility

                float networkSentBytes = _networkSentCounter?.NextValue() ?? 0;
                float networkReceivedBytes = _networkReceivedCounter?.NextValue() ?? 0;
                float networkActivity = Math.Min(100, (networkSentBytes + networkReceivedBytes) / (1024 * 1024) * 10); // Scale for visibility

                // Get GPU usage and Battery info if available
                float gpuUsage = GetGpuUsage();
                float batteryPercentage = GetBatteryPercentage();

                // Update chart data
                UpdateChartData(cpuUsage, ramUsage, diskActivity, networkActivity, gpuUsage, batteryPercentage);

                // Update UI elements
                UpdateStatusBarInfo(cpuUsage, ramUsage, diskActivity);
                UpdateCurrentResourceDisplay();
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Performance monitoring error: {ex.Message}";
            }
        }

        private float GetGpuUsage()
        {
            // Simplified method stub - actual implementation would use DirectX or similar
            // This is just a placeholder
            Random rand = new Random();
            return (float)rand.NextDouble() * 50; // Random usage between 0 and 50%
        }

        private float GetBatteryPercentage()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                foreach (var obj in searcher.Get())
                {
                    int estimatedChargeRemaining = Convert.ToInt32(obj["EstimatedChargeRemaining"]);
                    return estimatedChargeRemaining;
                }
            }
            catch { }
            return 0;
        }

        private void UpdateChartData(float cpuUsage, float ramUsage, float diskActivity,
                                    float networkActivity, float gpuUsage, float batteryPercentage)
        {
            // Update the chart values
            _cpuValues.Add(cpuUsage);
            _ramValues.Add(ramUsage);
            _diskValues.Add(diskActivity);
            _networkValues.Add(networkActivity);
            _gpuValues.Add(gpuUsage);
            _batteryValues.Add(batteryPercentage);

            // Remove oldest values to maintain fixed length
            if (_cpuValues.Count > MAX_POINTS)
                _cpuValues.RemoveAt(0);
            if (_ramValues.Count > MAX_POINTS)
                _ramValues.RemoveAt(0);
            if (_diskValues.Count > MAX_POINTS)
                _diskValues.RemoveAt(0);
            if (_networkValues.Count > MAX_POINTS)
                _networkValues.RemoveAt(0);
            if (_gpuValues.Count > MAX_POINTS)
                _gpuValues.RemoveAt(0);
            if (_batteryValues.Count > MAX_POINTS)
                _batteryValues.RemoveAt(0);
        }

        private void UpdateStatusBarInfo(float cpuUsage, float ramUsage, float diskActivity)
        {
            CPUUsageText.Text = $"CPU: {cpuUsage:F1}%";
            MemoryUsageText.Text = $"Memory: {ramUsage:F1}%";
            DiskUsageText.Text = $"Disk: {diskActivity:F1}%";
        }

        private void UpdateCurrentResourceDisplay()
        {
            // Check if UI elements exist before using them
            if (PerformanceResourceName == null ||
                PerformanceResourceUtilization == null ||
                PerformanceResourceValue == null)
            {
                // UI elements not ready yet, exit the method
                return;
            }

            switch (_selectedResource)
            {
                case "CPU":
                    PerformanceResourceName.Text = "CPU";
                    PerformanceResourceUtilization.Text = $"{_cpuValues[_cpuValues.Count - 1]:F1}% Utilization";
                    PerformanceResourceValue.Text = $"{_cpuValues[_cpuValues.Count - 1]:F1}%";
                    UpdateCpuDetails();
                    break;
                case "Memory":
                    PerformanceResourceName.Text = "Memory";
                    PerformanceResourceUtilization.Text = $"{_ramValues[_ramValues.Count - 1]:F1}% In Use";
                    PerformanceResourceValue.Text = $"{_ramValues[_ramValues.Count - 1]:F1}%";
                    UpdateMemoryDetails();
                    break;
                case "Disk":
                    PerformanceResourceName.Text = "Disk";
                    PerformanceResourceUtilization.Text = $"{_diskValues[_diskValues.Count - 1]:F1}% Activity";
                    PerformanceResourceValue.Text = $"{_diskValues[_diskValues.Count - 1]:F1}%";
                    UpdateDiskDetails();
                    break;
                case "Network":
                    PerformanceResourceName.Text = "Network";
                    PerformanceResourceUtilization.Text = $"{_networkValues[_networkValues.Count - 1]:F1}% Activity";
                    PerformanceResourceValue.Text = $"{_networkValues[_networkValues.Count - 1]:F1}%";
                    UpdateNetworkDetails();
                    break;
                case "GPU":
                    PerformanceResourceName.Text = "GPU";
                    PerformanceResourceUtilization.Text = $"GPU Utilization";
                    PerformanceResourceValue.Text = $"{_gpuValues[_gpuValues.Count - 1]:F1}%";
                    UpdateGpuDetails();
                    break;
                case "Battery":
                    PerformanceResourceName.Text = "Battery";
                    PerformanceResourceUtilization.Text = $"Battery Remaining";
                    PerformanceResourceValue.Text = $"{_batteryValues[_batteryValues.Count - 1]:F1}%";
                    UpdateBatteryDetails();
                    break;
            }
        }

        private void UpdateSystemInfo()
        {
            try
            {
                string osInfo = Environment.OSVersion.VersionString;
                string cpuInfo = "Unknown CPU";
                string memoryInfo = "Unknown RAM";

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                    foreach (var obj in searcher.Get())
                    {
                        cpuInfo = obj["Name"].ToString();
                        break;
                    }

                    using var memSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                    foreach (var obj in memSearcher.Get())
                    {
                        ulong totalMemoryKB = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                        memoryInfo = $"{totalMemoryKB / (1024 * 1024 * 1024):N1} GB RAM";
                        break;
                    }
                }
                catch
                {
                    // Use defaults if WMI fails
                }

                SystemInfoText.Text = $"{Environment.MachineName} | {osInfo} | {cpuInfo} | {memoryInfo}";
            }
            catch (Exception ex)
            {
                SystemInfoText.Text = $"Error retrieving system info: {ex.Message}";
            }
        }

        private void UpdateCpuDetails()
        {
            PerformanceDetailsLeft.Children.Clear();
            PerformanceDetailsRight.Children.Clear();

            // Left panel
            AddDetailItem(PerformanceDetailsLeft, "Utilization", $"{_cpuValues[_cpuValues.Count - 1]:F1}%", 0);
            AddDetailItem(PerformanceDetailsLeft, "Processes", $"{Process.GetProcesses().Length}", 1);
            AddDetailItem(PerformanceDetailsLeft, "Threads", GetTotalThreadCount().ToString(), 2);
            AddDetailItem(PerformanceDetailsLeft, "Handles", "N/A", 3);

            // Right panel
            AddDetailItem(PerformanceDetailsRight, "Cores", Environment.ProcessorCount.ToString(), 0);
            AddDetailItem(PerformanceDetailsRight, "Logical Processors", Environment.ProcessorCount.ToString(), 1);
            AddDetailItem(PerformanceDetailsRight, "Virtualization", "Enabled", 2);
            AddDetailItem(PerformanceDetailsRight, "Up Time", GetSystemUptime(), 3);
        }

        private void UpdateMemoryDetails()
        {
            PerformanceDetailsLeft.Children.Clear();
            PerformanceDetailsRight.Children.Clear();

            // Get memory information
            ulong totalMemoryBytes = 0;
            ulong availableMemoryBytes = 0;

            try
            {
                using var csSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (var obj in csSearcher.Get())
                {
                    totalMemoryBytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                    break;
                }

                using var osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (var obj in osSearcher.Get())
                {
                    availableMemoryBytes = Convert.ToUInt64(obj["FreePhysicalMemory"]) * 1024;
                    break;
                }
            }
            catch
            {
                // Use defaults if WMI fails
            }

            double totalMemoryGB = totalMemoryBytes / (1024.0 * 1024.0 * 1024.0);
            double usedMemoryGB = (totalMemoryBytes - availableMemoryBytes) / (1024.0 * 1024.0 * 1024.0);
            double availableMemoryGB = availableMemoryBytes / (1024.0 * 1024.0 * 1024.0);

            // Left panel
            AddDetailItem(PerformanceDetailsLeft, "In Use", $"{usedMemoryGB:F1} GB", 0);
            AddDetailItem(PerformanceDetailsLeft, "Available", $"{availableMemoryGB:F1} GB", 1);
            AddDetailItem(PerformanceDetailsLeft, "Used %", $"{_ramValues[_ramValues.Count - 1]:F1}%", 2);
            AddDetailItem(PerformanceDetailsLeft, "Cached", "N/A", 3);

            // Right panel
            AddDetailItem(PerformanceDetailsRight, "Total", $"{totalMemoryGB:F1} GB", 0);
            AddDetailItem(PerformanceDetailsRight, "Slots Used", "N/A", 1);
            AddDetailItem(PerformanceDetailsRight, "Form Factor", "N/A", 2);
            AddDetailItem(PerformanceDetailsRight, "Hardware Reserved", "N/A", 3);
        }

        private void UpdateDiskDetails()
        {
            PerformanceDetailsLeft.Children.Clear();
            PerformanceDetailsRight.Children.Clear();

            float readBytes = _diskReadCounter?.NextValue() ?? 0;
            float writeBytes = _diskWriteCounter?.NextValue() ?? 0;

            // Left panel
            AddDetailItem(PerformanceDetailsLeft, "Active Time", $"{_diskValues[_diskValues.Count - 1]:F1}%", 0);
            AddDetailItem(PerformanceDetailsLeft, "Read Speed", $"{readBytes / (1024 * 1024):F2} MB/s", 1);
            AddDetailItem(PerformanceDetailsLeft, "Write Speed", $"{writeBytes / (1024 * 1024):F2} MB/s", 2);
            AddDetailItem(PerformanceDetailsLeft, "Avg. Response Time", "N/A", 3);

            // Right panel - get disk information
            string diskCapacity = "N/A";
            string diskFree = "N/A";

            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                if (drives.Length > 0 && drives[0].IsReady)
                {
                    double totalSizeGB = drives[0].TotalSize / (1024.0 * 1024 * 1024);
                    double freeSpaceGB = drives[0].AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    diskCapacity = $"{totalSizeGB:F1} GB";
                    diskFree = $"{freeSpaceGB:F1} GB";
                }
            }
            catch
            {
                // Use defaults if drive info fails
            }

            AddDetailItem(PerformanceDetailsRight, "Capacity", diskCapacity, 0);
            AddDetailItem(PerformanceDetailsRight, "Free Space", diskFree, 1);
            AddDetailItem(PerformanceDetailsRight, "Type", "SSD", 2);
            AddDetailItem(PerformanceDetailsRight, "System Disk", "Yes", 3);
        }

        private void UpdateNetworkDetails()
        {
            PerformanceDetailsLeft.Children.Clear();
            PerformanceDetailsRight.Children.Clear();

            float sentBytes = _networkSentCounter?.NextValue() ?? 0;
            float receivedBytes = _networkReceivedCounter?.NextValue() ?? 0;

            // Calculate network speeds in KB/s or MB/s
            string sentSpeed = FormatNetworkSpeed(sentBytes);
            string recvSpeed = FormatNetworkSpeed(receivedBytes);

            // Left panel
            AddDetailItem(PerformanceDetailsLeft, "Send", sentSpeed, 0);
            AddDetailItem(PerformanceDetailsLeft, "Receive", recvSpeed, 1);
            AddDetailItem(PerformanceDetailsLeft, "Activity", $"{_networkValues[_networkValues.Count - 1]:F1}%", 2);
            AddDetailItem(PerformanceDetailsLeft, "Connection Type", "Ethernet", 3);

            // Right panel
            string ipAddress = "N/A";
            try
            {
                ipAddress = NetworkInterface
                    .GetAllNetworkInterfaces()[0].GetIPProperties()
                    .UnicastAddresses[0].Address.ToString();
            }
            catch { }

            AddDetailItem(PerformanceDetailsRight, "Interface", GetNetworkInterfaceName(), 0);
            AddDetailItem(PerformanceDetailsRight, "IP Address", ipAddress, 1);
            AddDetailItem(PerformanceDetailsRight, "State", "Connected", 2);
            AddDetailItem(PerformanceDetailsRight, "Adapter Name", "N/A", 3);
        }

        private string GetNetworkInterfaceName()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()[0].Name;
            }
            catch
            {
                return "Unknown";
            }
        }

        private void UpdateGpuDetails()
        {
            PerformanceDetailsLeft.Children.Clear();
            PerformanceDetailsRight.Children.Clear();

            // Left panel - placeholder for GPU info
            AddDetailItem(PerformanceDetailsLeft, "GPU", "N/A", 0);
            AddDetailItem(PerformanceDetailsLeft, "Utilization", $"{_gpuValues[_gpuValues.Count - 1]:F1}%", 1);
            AddDetailItem(PerformanceDetailsLeft, "Memory Used", "N/A", 2);
            AddDetailItem(PerformanceDetailsLeft, "Temperature", "N/A", 3);

            // Right panel
            AddDetailItem(PerformanceDetailsRight, "Total Memory", "N/A", 0);
            AddDetailItem(PerformanceDetailsRight, "Driver Version", "N/A", 1);
            AddDetailItem(PerformanceDetailsRight, "Driver Date", "N/A", 2);
            AddDetailItem(PerformanceDetailsRight, "Resolution", $"{SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight}", 3);
        }

        private void UpdateBatteryDetails()
        {
            PerformanceDetailsLeft.Children.Clear();
            PerformanceDetailsRight.Children.Clear();

            // Get battery information
            bool batteryPresent = false;
            string batteryState = "Unknown";
            string timeRemaining = "Unknown";

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                foreach (var obj in searcher.Get())
                {
                    batteryPresent = true;
                    ushort batteryStatus = Convert.ToUInt16(obj["BatteryStatus"]);
                    batteryState = batteryStatus == 2 ? "Charging" : "Discharging";

                    int estimatedRunTime = 0;
                    if (obj["EstimatedRunTime"] != null)
                    {
                        estimatedRunTime = Convert.ToInt32(obj["EstimatedRunTime"]);
                        if (estimatedRunTime > 0)
                        {
                            int hrs = estimatedRunTime / 60;
                            int mins = estimatedRunTime % 60;
                            timeRemaining = $"{hrs}:{mins:D2}";
                        }
                    }
                }
            }
            catch { }

            // Left panel
            AddDetailItem(PerformanceDetailsLeft, "Charge", $"{_batteryValues[_batteryValues.Count - 1]:F1}%", 0);
            AddDetailItem(PerformanceDetailsLeft, "State", batteryState, 1);
            AddDetailItem(PerformanceDetailsLeft, "Present", batteryPresent ? "Yes" : "No", 2);
            AddDetailItem(PerformanceDetailsLeft, "Active Power Plan", "Balanced", 3);

            // Right panel
            AddDetailItem(PerformanceDetailsRight, "Time Remaining", timeRemaining, 0);
            AddDetailItem(PerformanceDetailsRight, "Full Charge Capacity", "N/A", 1);
            AddDetailItem(PerformanceDetailsRight, "Design Capacity", "N/A", 2);
            AddDetailItem(PerformanceDetailsRight, "Cycle Count", "N/A", 3);
        }

        private int GetTotalThreadCount()
        {
            try
            {
                int totalThreads = 0;
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        totalThreads += proc.Threads.Count;
                    }
                    catch
                    {
                        // Skip if can't access thread information
                    }
                }
                return totalThreads;
            }
            catch
            {
                return 0;
            }
        }

        private string GetSystemUptime()
        {
            try
            {
                TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                if (uptime.TotalDays >= 1)
                    return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
                else
                    return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string FormatNetworkSpeed(float bytesPerSec)
        {
            if (bytesPerSec >= 1024 * 1024)
                return $"{bytesPerSec / (1024 * 1024):F2} MB/s";
            else
                return $"{bytesPerSec / 1024:F2} KB/s";
        }

        private void AddDetailItem(Grid grid, string label, string value, int row)
        {
            // Define row if needed
            if (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            // Create a grid for this row
            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Create label TextBlock
            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.LightGray),
                Margin = new Thickness(0, 3, 0, 3)
            };

            // Create value TextBlock
            var valueBlock = new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 3)
            };

            // Add TextBlocks to the grid
            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);
            rowGrid.Children.Add(labelBlock);
            rowGrid.Children.Add(valueBlock);

            // Add the row grid to the parent grid
            Grid.SetRow(rowGrid, row);
            grid.Children.Add(rowGrid);
        }

        // Event handler for resource selection
        public void PerformanceResource_Checked(object sender, RoutedEventArgs e)
        {
            var radioButton = sender as RadioButton;
            if (radioButton != null && radioButton.Tag != null)
            {
                _selectedResource = radioButton.Tag.ToString();
                SetupChartForResource(_selectedResource);
                UpdateCurrentResourceDisplay();
            }
        }

        // Public method to clean up when view is unloaded
        public void Cleanup()
        {
            _performanceUpdateTimer?.Stop();
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            _networkSentCounter?.Dispose();
            _networkReceivedCounter?.Dispose();
        }
    }
}
