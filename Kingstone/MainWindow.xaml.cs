using Kingstone.utils;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Kingstone
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CameraVideoDisplay cameraDisplay;
        private CompleteInputInterceptor inputInterceptor;
        private ComPortQueueManager comPortQueueManager;
        private bool isControlsVisible = true;
        private bool isSystemActive = false;
        private int scrollSensitive = 5;

        public MainWindow()
        {
            InitializeComponent();
            InitializeComponents();
            LoadSettings();
        }

        private void InitializeComponents()
        {
            // Initialize camera display
            cameraDisplay = new CameraVideoDisplay(CameraImage);
            cameraDisplay.StatusChanged += OnCameraStatusChanged;

            // Initialize input interceptor
            inputInterceptor = new CompleteInputInterceptor();
            inputInterceptor.KeyEvent += OnKeyEvent;
            // inputInterceptor.MouseEvent += OnMouseEvent;

            // Initialize COM port queue manager
            comPortQueueManager = new ComPortQueueManager();
            comPortQueueManager.StatusChanged += OnComPortStatusChanged;
            comPortQueueManager.ConnectionChanged += OnComPortConnectionChanged;
            comPortQueueManager.MessageSent += ComPortQueueManager_MessageSent;

            // Setup floating controls events
            FloatingControls.CameraSelected += OnCameraSelected;
            FloatingControls.StartStopToggled += OnStartStopToggled;
            FloatingControls.HotkeySendRequested += OnHotkeySendRequested;
            FloatingControls.ResolutionSet += OnResolutionSet;
            FloatingControls.ScrollSensitivityChanged += OnScrollSensitivityChanged;

            // Load available cameras
            var cameras = cameraDisplay.GetAvailableCameras();
            FloatingControls.SetCameraList(cameras);

            // Set initial status
            FloatingControls.SetStatus("Ready");
        }

        private void ComPortQueueManager_MessageSent(object? sender, ComPortMessage e)
        {

            if (e.Command[0] != 0x33) return;

            string str = "";

            for (int i = 0; i < e.Command.Length; i ++) str += e.Command[i].ToString("X2") + " ";

            Console.WriteLine($"Send Command: {str}");
        }

        private void LoadSettings()
        {
            UpdateControlsVisibility(false); // Load without animation
        }

        private void OnScrollSensitivityChanged(object sender, int sensitivity)
        {
            scrollSensitive = sensitivity;
        }

        private void FloatingToggleButton_Click(object sender, RoutedEventArgs e)
        {
            isControlsVisible = !isControlsVisible;
            UpdateControlsVisibility(true);

            if (isSystemActive && comPortQueueManager.IsConnected)
            {
                if (isControlsVisible)
                {
                    inputInterceptor.StopIntercepting();
                }
                else
                {
                    inputInterceptor.StartIntercepting();
                }
            }
        }

        private void UpdateControlsVisibility(bool animate)
        {
            if (animate)
            {
                var storyboard = new Storyboard();

                // Animate button position
                var buttonMarginAnimation = new ThicknessAnimation
                {
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };

                var panelMarginAnimation = new ThicknessAnimation
                {
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };

                if (isControlsVisible)
                {
                    FloatingControls.Visibility = Visibility.Visible;
                    panelMarginAnimation.From = new Thickness(0, 0, -250, 0);
                    panelMarginAnimation.To = new Thickness(0, 0, 0, 0);

                    // Move button to left of controls bar
                    buttonMarginAnimation.From = new Thickness(0, 0, 0, 0);
                    buttonMarginAnimation.To = new Thickness(0, 0, 250, 0);
                }
                else
                {
                    panelMarginAnimation.From = new Thickness(0, 0, 0, 0);
                    panelMarginAnimation.To = new Thickness(0, 0, -250, 0);
                    panelMarginAnimation.Completed += (s, e) => FloatingControls.Visibility = Visibility.Hidden;

                    // Move button to right edge
                    buttonMarginAnimation.From = new Thickness(0, 0, 250, 0);
                    buttonMarginAnimation.To = new Thickness(0, 0, 0, 0);
                }

                // Apply animations
                Storyboard.SetTarget(panelMarginAnimation, FloatingControls);
                Storyboard.SetTargetProperty(panelMarginAnimation, new PropertyPath(MarginProperty));
                storyboard.Children.Add(panelMarginAnimation);

                Storyboard.SetTarget(buttonMarginAnimation, FloatingToggleButton);
                Storyboard.SetTargetProperty(buttonMarginAnimation, new PropertyPath(MarginProperty));
                storyboard.Children.Add(buttonMarginAnimation);

                storyboard.Begin();
            }
            else
            {
                FloatingControls.Visibility = isControlsVisible ? Visibility.Visible : Visibility.Hidden;
                FloatingControls.Opacity = isControlsVisible ? 1 : 0;

                // Set button position without animation
                FloatingToggleButton.Margin = isControlsVisible ?
                    new Thickness(0, 0, 250, 0) :
                    new Thickness(0, 0, 0, 0);
            }
        }

        private void OnCameraSelected(object sender, CameraSelectionEventArgs e)
        {
            Task.Run(() => cameraDisplay.StartCamera(e.Camera.Index));
        }

        private void OnStartStopToggled(object sender, bool shouldStart)
        {
            if (shouldStart)
            {
                var selectedPort = FloatingControls.ComPortComboBox.SelectedItem as ComPortInfo;
                if (selectedPort == null)
                {
                    MessageBox.Show("Please select a COM port first.", "COM Port Required",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (comPortQueueManager.Connect(selectedPort.PortName))
                {
                    // inputInterceptor.StartIntercepting();
                    isSystemActive = true;
                    FloatingControls.SetStartStopState(true);
                    FloatingControls.SetStatus("System Active - Input Intercepted");
                }
            }
            else
            {
                // inputInterceptor.StopIntercepting();
                comPortQueueManager.Disconnect();
                isSystemActive = false;
                FloatingControls.SetStartStopState(false);
                FloatingControls.SetStatus("System Stopped");
            }
        }

        private void OnHotkeySendRequested(object sender, string hotkeyType)
        {
            if (comPortQueueManager.IsConnected)
            {
                comPortQueueManager.QueueHotkey(hotkeyType);
                FloatingControls.SetStatus($"Sent: {hotkeyType}");
            }
        }

        private void OnResolutionSet(object sender, ResolutionEventArgs e)
        {
            if (comPortQueueManager.IsConnected)
            {
                FloatingControls.SetStatus($"Resolution set: ({e.X:F1}, {e.Y:F1})");
            }
        }

        private void OnKeyEvent(int vkCode, bool isKeyDown, bool isSystemKey)
        {
            if (isSystemActive && comPortQueueManager.IsConnected)
            {
                comPortQueueManager.QueueKeyboardEvent(isKeyDown ? KeyboardEvent.Down : KeyboardEvent.Up, vkCode, isSystemKey);
            }
        }

        /*private void OnMouseEvent(CompleteMouseInterceptor.MouseEventInfo mouseInfo)
        {
            if (isSystemActive && comPortQueueManager.IsConnected && !isControlsVisible)
            {
                string eventType = mouseInfo.EventType.ToString();
                int button = (int)mouseInfo.Button;
                comPortQueueManager.SendMouseEvent(eventType, mouseInfo.X, mouseInfo.Y, button, mouseInfo.WheelDelta);
            }
        }*/

        private void OnCameraStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (!isSystemActive)
                    FloatingControls.SetStatus($"Camera: {status}");
            });
        }

        private void OnComPortStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                FloatingControls.SetStatus(status, status.Contains("error") || status.Contains("failed"));
            });
        }

        private void OnComPortConnectionChanged(object sender, bool isConnected)
        {
            // Handle connection state changes if needed
        }

        // Fallback input handling when interceptor is not active
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (!isSystemActive)
                return;

            // Additional key handling if needed
        }

        private void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Focus the window to ensure it receives input
            this.Focus();
        }

        private void MainWindow_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Additional mouse handling if needed
        }

        private void MainWindow_MouseMove(object sender, MouseEventArgs e)
        {
            // Additional mouse move handling if needed
        }

        private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Additional mouse wheel handling if needed
        }

        protected override void OnClosed(EventArgs e)
        {
            inputInterceptor?.StopIntercepting();
            cameraDisplay?.Dispose();
            comPortQueueManager?.Dispose();
            base.OnClosed(e);
        }

        private System.Windows.Point GetRealImagePosition(System.Windows.Point mousePosition)
        {
            if (CameraImage.Source == null)
                return new System.Windows.Point(0, 0);

            try
            {
                // Get control dimensions
                double controlWidth = CameraImage.ActualWidth;
                double controlHeight = CameraImage.ActualHeight;

                if (controlWidth == 0 || controlHeight == 0)
                    return new System.Windows.Point(0, 0);

                return new System.Windows.Point(mousePosition.X / controlWidth * 0x7FFF, mousePosition.Y / controlHeight * 0x7FFF);
            }
            catch (Exception ex)
            {
                return new System.Windows.Point(0, 0);
            }
        }

        private void CameraImage_MouseMove(object sender, MouseEventArgs e)
        {
            // Console.WriteLine($"Mouse Move {e.GetPosition(CameraImage).X}, {e.GetPosition(CameraImage).Y}");

            if (!isSystemActive || !comPortQueueManager.IsConnected || isControlsVisible) return;

            Point pos = GetRealImagePosition(e.GetPosition(CameraImage));

            comPortQueueManager.QueueMouseEvent(MouseEvent.Move, pos);
        }

        private void CameraImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isSystemActive || !comPortQueueManager.IsConnected || isControlsVisible) return;

            Point pos = GetRealImagePosition(e.GetPosition(CameraImage));

            comPortQueueManager.QueueMouseEvent(MouseEvent.LDown, pos);
        }

        private void CameraImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isSystemActive || !comPortQueueManager.IsConnected || isControlsVisible) return;

            Point pos = GetRealImagePosition(e.GetPosition(CameraImage));

            comPortQueueManager.QueueMouseEvent(MouseEvent.LUp, pos);
        }

        private void CameraImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isSystemActive || !comPortQueueManager.IsConnected || isControlsVisible) return;

            Point pos = GetRealImagePosition(e.GetPosition(CameraImage));

            comPortQueueManager.QueueMouseEvent(MouseEvent.RDown, pos);
        }

        private void CameraImage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isSystemActive || !comPortQueueManager.IsConnected || isControlsVisible) return;

            Point pos = GetRealImagePosition(e.GetPosition(CameraImage));

            comPortQueueManager.QueueMouseEvent(MouseEvent.RUp, pos);
        }

        private void CameraImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!isSystemActive || !comPortQueueManager.IsConnected || isControlsVisible) return;

            Point pos = GetRealImagePosition(e.GetPosition(CameraImage));

            comPortQueueManager.QueueMouseEvent(MouseEvent.Scroll, pos, e.Delta > 0 ? scrollSensitive : -scrollSensitive);
        }
    }
}