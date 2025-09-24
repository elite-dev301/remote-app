using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public class CameraVideoDisplay
{
    private System.Windows.Controls.Image imageControl;
    private VideoCapture _capture;
    private Dispatcher dispatcher;

    private Thread _cameraThread;
    private volatile bool _isCapturing;

    public event EventHandler<string> StatusChanged;


    public class CameraDevice
    {
        public string Name { get; set; }
        public int Index { get; set; }
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
            var cameraNames = new List<string>();
            // Use ManagementObjectSearcher to find devices with PNPClass "Image" or "Camera"
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE (PNPClass = 'Image' OR PNPClass = 'Camera')"))
            {
                int i = 0;
                foreach (var device in searcher.Get())
                {
                    cameras.Add(new CameraDevice
                    {
                        Name = device["Caption"].ToString() ?? "No Name",
                        Index = i ++
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error getting cameras: {ex.Message}");
        }

        return cameras;
    }

    public void StartCamera(int index)
    {
        try
        {

            StatusChanged?.Invoke(this, "Camera starting");

            StopCamera();

            _capture = new VideoCapture(index);

            _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
            _capture.Set(VideoCaptureProperties.FrameHeight, 1080);

            _isCapturing = true;
            _cameraThread = new Thread(ProcessCameraFrames)
            {
                IsBackground = true
            };
            _cameraThread.Start();

            StatusChanged?.Invoke(this, "Camera started");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error starting camera: {ex.Message}");
        }
    }

    private void ProcessCameraFrames()
    {
        using (var frame = new Mat())
        {
            while (_isCapturing)
            {
                _capture.Read(frame);
                if (frame.Empty())
                {
                    continue;
                }

                // Convert the frame to a WriteableBitmap on the UI thread
                dispatcher.Invoke(() =>
                {
                    try
                    {
                        imageControl.Source = frame.ToWriteableBitmap();
                    }
                    catch
                    {
                        // Ignore any exceptions during conversion
                    }
                });

                // Add a small delay to control the frame rate
                Thread.Sleep(30);
            }
        }
    }

    public void StopCamera()
    {
        _isCapturing = false;
        _cameraThread?.Join(10);
        _capture?.Release();
        _capture?.Dispose();
    }

    public void Dispose()
    {
        StopCamera();
    }
}