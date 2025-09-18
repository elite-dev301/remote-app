using AForge.Video;
using AForge.Video.DirectShow;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public class CameraVideoDisplay
{
    private FilterInfoCollection videoDevices;
    private VideoCaptureDevice videoSource;
    private System.Windows.Controls.Image imageControl;
    private bool isRecording = false;
    private Dispatcher dispatcher;

    public event EventHandler<string> StatusChanged;
    public event EventHandler<System.Drawing.Size> ResolutionChanged;

    public class CameraDevice
    {
        public string Name { get; set; }
        public string MonikerString { get; set; }
        public override string ToString() => Name;
    }

    public class VideoCapability
    {
        public System.Drawing.Size FrameSize { get; set; }
        public int FrameRate { get; set; }
        public override string ToString() => $"{FrameSize.Width}x{FrameSize.Height} @ {FrameRate}fps";
    }

    public CameraVideoDisplay(System.Windows.Controls.Image imageControl)
    {
        this.imageControl = imageControl;
        this.dispatcher = imageControl.Dispatcher;

        // Set initial scaling mode for aspect ratio preservation
        imageControl.Stretch = Stretch.Uniform; // Preserves aspect ratio
        imageControl.StretchDirection = StretchDirection.Both;
    }

    public List<CameraDevice> GetAvailableCameras()
    {
        var cameras = new List<CameraDevice>();

        try
        {
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            foreach (FilterInfo device in videoDevices)
            {
                cameras.Add(new CameraDevice
                {
                    Name = device.Name,
                    MonikerString = device.MonikerString
                });
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error getting cameras: {ex.Message}");
        }

        return cameras;
    }

    public List<VideoCapability> GetCameraCapabilities(string monikerString)
    {
        var capabilities = new List<VideoCapability>();

        try
        {
            var device = new VideoCaptureDevice(monikerString);

            foreach (VideoCapabilities capability in device.VideoCapabilities)
            {
                capabilities.Add(new VideoCapability
                {
                    FrameSize = capability.FrameSize,
                    FrameRate = capability.AverageFrameRate
                });
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error getting capabilities: {ex.Message}");
        }

        return capabilities;
    }

    public void StartCamera(string monikerString, System.Drawing.Size? resolution = null)
    {
        try
        {
            StopCamera();

            videoSource = new VideoCaptureDevice(monikerString);

            // Set resolution if specified
            if (resolution.HasValue)
            {
                var capability = videoSource.VideoCapabilities
                    .FirstOrDefault(c => c.FrameSize == resolution.Value);

                if (capability != null)
                {
                    videoSource.VideoResolution = capability;
                }
            }

            videoSource.NewFrame += OnNewFrame;
            videoSource.VideoSourceError += OnVideoSourceError;

            videoSource.Start();
            isRecording = true;

            StatusChanged?.Invoke(this, "Camera started");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error starting camera: {ex.Message}");
        }
    }

    public void StopCamera()
    {
        try
        {
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.WaitForStop();

                videoSource.NewFrame -= OnNewFrame;
                videoSource.VideoSourceError -= OnVideoSourceError;
                videoSource = null;
            }

            isRecording = false;
            StatusChanged?.Invoke(this, "Camera stopped");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error stopping camera: {ex.Message}");
        }
    }

    private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        try
        {
            // Clone the frame to avoid disposal issues
            var bitmap = new Bitmap(eventArgs.Frame);

            // Convert to WPF-compatible format
            var bitmapImage = BitmapToImageSource(bitmap);

            // Update UI on main thread
            dispatcher.BeginInvoke(new Action(() =>
            {
                imageControl.Source = bitmapImage;
                ResolutionChanged?.Invoke(this, new System.Drawing.Size(bitmap.Width, bitmap.Height));
            }));

            bitmap.Dispose();
        }
        catch (Exception ex)
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                StatusChanged?.Invoke(this, $"Frame processing error: {ex.Message}");
            }));
        }
    }

    private void OnVideoSourceError(object sender, VideoSourceErrorEventArgs eventArgs)
    {
        dispatcher.BeginInvoke(new Action(() =>
        {
            StatusChanged?.Invoke(this, $"Video source error: {eventArgs.Description}");
        }));
    }

    private BitmapImage BitmapToImageSource(Bitmap bitmap)
    {
        using (var memory = new MemoryStream())
        {
            bitmap.Save(memory, ImageFormat.Bmp);
            memory.Position = 0;

            var bitmapimage = new BitmapImage();
            bitmapimage.BeginInit();
            bitmapimage.StreamSource = memory;
            bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapimage.EndInit();
            bitmapimage.Freeze();

            return bitmapimage;
        }
    }

    public bool IsRecording => isRecording;

    public void SetScalingMode(Stretch stretchMode)
    {
        imageControl.Stretch = stretchMode;
    }

    public void TakeSnapshot(string filePath)
    {
        try
        {
            if (imageControl.Source is BitmapSource bitmapSource)
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(fileStream);
                }

                StatusChanged?.Invoke(this, $"Snapshot saved: {filePath}");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Snapshot error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopCamera();
    }
}