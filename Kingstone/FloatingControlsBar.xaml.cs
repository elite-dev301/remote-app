using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Kingstone
{
    /// <summary>
    /// Interaction logic for FloatingControlsBar.xaml
    /// </summary>
    public partial class FloatingControlsBar : UserControl
    {
        public event EventHandler<CameraSelectionEventArgs> CameraSelected;
        public event EventHandler<ComPortEventArgs> ComPortSelected;
        public event EventHandler<bool> StartStopToggled;
        public event EventHandler<string> HotkeySendRequested;
        public event EventHandler<ResolutionEventArgs> ResolutionSet;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<int> ScrollSensitivityChanged;
        public event EventHandler<bool> SetFullScreen;
        public event EventHandler<bool> ScrollReverseChanged;

        private bool isStarted = false;

        private int scrollSensitivity = 3; // Default value

        public int ScrollSensitivity => scrollSensitivity;

        public FloatingControlsBar()
        {
            InitializeComponent();
            RefreshComPorts();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load saved settings from application settings
            WidthResolutionTextBox.Text = Properties.Settings.Default.WidthResolution.ToString();
            HeightResolutionTextBox.Text = Properties.Settings.Default.HeightResolution.ToString();

            if (!string.IsNullOrEmpty(Properties.Settings.Default.SelectedComPort))
            {
                ComPortComboBox.Text = Properties.Settings.Default.SelectedComPort;
            }

            if (!string.IsNullOrEmpty(Properties.Settings.Default.SelectedCamera))
            {
                CameraComboBox.Text = Properties.Settings.Default.SelectedCamera;
            }

            ReverseScrollCheckBox.IsChecked = Properties.Settings.Default.ScrollReverse;

            // Load scroll sensitivity
            scrollSensitivity = Properties.Settings.Default.ScrollSensitivity;
            if (scrollSensitivity < 1 || scrollSensitivity > 10)
                scrollSensitivity = 3; // Default value

            ScrollSensitivitySlider.Value = scrollSensitivity;
            ScrollSensitivityValue.Text = scrollSensitivity.ToString();
        }

        private void SaveSettings()
        {
            if (WidthResolutionTextBox != null && float.TryParse(WidthResolutionTextBox.Text, out float x))
                Properties.Settings.Default.WidthResolution = x;
            if (HeightResolutionTextBox != null && float.TryParse(HeightResolutionTextBox.Text, out float y))
                Properties.Settings.Default.HeightResolution = y;

            Properties.Settings.Default.SelectedComPort = ComPortComboBox.Text;
            Properties.Settings.Default.SelectedCamera = CameraComboBox.Text;
            Properties.Settings.Default.ScrollSensitivity = scrollSensitivity;
            Properties.Settings.Default.ScrollReverse = ReverseScrollCheckBox.IsChecked!.Value;
            Properties.Settings.Default.Save();
        }

        public void SetCameraList(System.Collections.Generic.List<CameraVideoDisplay.CameraDevice> cameras)
        {
            CameraComboBox.ItemsSource = cameras;
            if (cameras.Count > 0)
                CameraComboBox.SelectedIndex = 0;
        }

        public void RefreshComPorts()
        {
            try
            {
                var comPorts = ComPortHelper.GetAvailableComPorts();
                ComPortComboBox.ItemsSource = comPorts;

                // Try to restore previously selected port
                if (!string.IsNullOrEmpty(Properties.Settings.Default.SelectedComPort))
                {
                    var previousPort = comPorts.FirstOrDefault(cp => cp.PortName == Properties.Settings.Default.SelectedComPort);
                    if (previousPort != null)
                    {
                        ComPortComboBox.SelectedItem = previousPort;
                    }
                }

                // Update status
                if (comPorts.Count == 0)
                {
                    SetStatus("No COM ports found", true);
                }
                else
                {
                    SetStatus($"Found {comPorts.Count} COM port(s)");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error refreshing ports: {ex.Message}", true);
            }
        }

        public void SetStatus(string status, bool isError = false)
        {
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = status;
                StatusTextBlock.Foreground = isError ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.DarkGreen;
            }
        }

        public void SetStartStopState(bool isStarted)
        {
            this.isStarted = isStarted;
            StartStopButton.Content = isStarted ? "⏹ Stop" : "▶ Start";
            StartStopButton.Background = isStarted ? System.Windows.Media.Brushes.LightCoral : System.Windows.Media.Brushes.LightGreen;
        }

        private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraComboBox.SelectedItem is CameraVideoDisplay.CameraDevice camera)
            {
                CameraSelected?.Invoke(this, new CameraSelectionEventArgs { Camera = camera });
            }
        }

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshComPorts();
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ComPortComboBox.Text))
            {
                MessageBox.Show("Please select a COM port first.", "COM Port Required",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartStopToggled?.Invoke(this, !isStarted);
            SaveSettings();
        }

        private void SendHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (HotkeyComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                HotkeySendRequested?.Invoke(this, selectedItem.Tag.ToString());
            }
        }

        private void SetResolutionButton_Click(object sender, RoutedEventArgs e)
        {
            if (float.TryParse(WidthResolutionTextBox.Text, out float x) &&
                float.TryParse(HeightResolutionTextBox.Text, out float y))
            {
                ResolutionSet?.Invoke(this, new ResolutionEventArgs { X = x, Y = y });
                SaveSettings();
            }
            else
            {
                MessageBox.Show("Please enter valid float numbers for X and Y Resolutions.",
                              "Invalid Resolution", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ComPortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComPortComboBox.SelectedItem is ComPortInfo selectedPort)
            {
                Properties.Settings.Default.SelectedComPort = selectedPort.PortName;
                Properties.Settings.Default.Save();

                SetStatus($"Selected: {selectedPort}");
            }
        }

        private void ScrollSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            scrollSensitivity = (int)e.NewValue;

            if (ScrollSensitivityValue != null) // Check if UI is initialized
            {
                ScrollSensitivityValue.Text = scrollSensitivity.ToString();
            }

            // Notify about the change
            ScrollSensitivityChanged?.Invoke(this, scrollSensitivity);

            // Update status
            SetStatus($"Scroll sensitivity set to {scrollSensitivity}");
        }

        private void FullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (FullScreenButton.Content as string == "⛶ Full Screen")
            {
                SetFullScreen?.Invoke(this, true);
                FullScreenButton.Content = "🗗 Exit Full Screen";
            } else
            {
                SetFullScreen?.Invoke(this, false);
                FullScreenButton.Content = "⛶ Full Screen";
            }
        }

        private void ReverseScrollCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ScrollReverseChanged?.Invoke(this, ReverseScrollCheckBox.IsChecked!.Value);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ScrollSensitivityChanged?.Invoke(this, scrollSensitivity);
            ScrollReverseChanged?.Invoke(this, ReverseScrollCheckBox.IsChecked!.Value);
        }
    }

    public class CameraSelectionEventArgs : EventArgs
    {
        public CameraVideoDisplay.CameraDevice Camera { get; set; }
    }

    public class ComPortEventArgs : EventArgs
    {
        public string PortName { get; set; }
    }

    public class ResolutionEventArgs : EventArgs
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
