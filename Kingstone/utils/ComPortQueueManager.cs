using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kingstone.utils
{
    public class ComPortMessage
    {
        public byte[] Command { get; set; }
        public int Priority { get; set; } = 0; // 0 = normal, higher = more important

        public ComPortMessage(byte[] command, int priority = 0)
        {
            Command = command;
            Priority = priority;
        }
    }

    public class ComPortQueueManager : IDisposable
    {
        private SerialPort serialPort;
        private readonly ConcurrentQueue<ComPortMessage> messageQueue = new ConcurrentQueue<ComPortMessage>();
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private Task workerTask;
        private bool isConnected = false;
        private bool disposed = false;

        // Statistics
        private long totalMessagesSent = 0;
        private long totalMessagesQueued = 0;
        private long messagesInQueue = 0;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<bool> ConnectionChanged;
        public event EventHandler<ComPortMessage> MessageSent;
        public event EventHandler<Exception> SendError;

        // Properties
        public bool IsConnected => isConnected && serialPort?.IsOpen == true;
        public long QueueCount => messagesInQueue;
        public long TotalSent => totalMessagesSent;
        public long TotalQueued => totalMessagesQueued;

        public bool Connect(string portName, int baudRate = 115200)
        {
            try
            {
                Disconnect();

                serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    RtsEnable = true,
                    DtrEnable = true,
                    WriteTimeout = 1000,
                    ReadTimeout = 1000
                };

                serialPort.Open();
                isConnected = true;

                cancellationTokenSource = new CancellationTokenSource();

                // Start background worker thread
                workerTask = Task.Run(ProcessMessageQueue, cancellationTokenSource.Token);

                StatusChanged?.Invoke(this, $"Connected to {portName}");
                ConnectionChanged?.Invoke(this, true);
                Debug.WriteLine($"COM port connected: {portName}");

                return true;
            }
            catch (Exception ex)
            {
                string errorMsg = $"Connection failed: {ex.Message}";
                StatusChanged?.Invoke(this, errorMsg);
                ConnectionChanged?.Invoke(this, false);
                Debug.WriteLine($"COM port connection error: {ex}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                isConnected = false;

                // Stop the worker thread
                cancellationTokenSource?.Cancel();
                workerTask?.Wait(2000); // Wait up to 2 seconds
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disconnect error: {ex}");
            }

            try { 

                // Close serial port
                if (serialPort?.IsOpen == true)
                {
                    serialPort.Close();
                }
                serialPort?.Dispose();
                serialPort = null;

                // Clear the queue
                while (messageQueue.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref messagesInQueue);
                }

                StatusChanged?.Invoke(this, "Disconnected");
                ConnectionChanged?.Invoke(this, false);
                Debug.WriteLine("COM port disconnected");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disconnect error: {ex}");
            }
        }

        // Main method to queue messages
        public void QueueMessage(byte[] command, int priority = 0, int count = 1)
        {
            if (disposed || !isConnected)
                return;

            try
            {
                var message = new ComPortMessage(command, priority);
                for (int i = 0; i < count; i++) { messageQueue.Enqueue(message); }
                Interlocked.Increment(ref messagesInQueue);
                Interlocked.Increment(ref totalMessagesQueued);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error queuing message: {ex}");
            }
        }

        // Background worker thread
        private async Task ProcessMessageQueue()
        {
            Debug.WriteLine("COM port message queue worker started");

            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (messageQueue.TryDequeue(out ComPortMessage message) &&
                        isConnected &&
                        serialPort?.IsOpen == true)
                    {
                        // Send the message
                        await SendMessageAsync(message);
                        Interlocked.Decrement(ref messagesInQueue);
                        Interlocked.Increment(ref totalMessagesSent);

                        // Notify message sent
                        MessageSent?.Invoke(this, message);

                        // Small delay to prevent overwhelming the receiving device
                        await Task.Delay(1, cancellationTokenSource.Token);
                    }
                    else
                    {
                        // No messages to process, wait a bit
                        await Task.Delay(5, cancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Message queue worker error: {ex}");
                    SendError?.Invoke(this, ex);

                    // Brief delay on error to prevent tight error loops
                    await Task.Delay(100, cancellationTokenSource.Token);
                }
            }

            Debug.WriteLine("COM port message queue worker stopped");
        }

        private async Task SendMessageAsync(ComPortMessage message)
        {
            if (serialPort?.IsOpen != true)
                return;

            try
            {
                byte[] data = message.Command;
                await serialPort.BaseStream.WriteAsync(data, 0, data.Length, cancellationTokenSource.Token);
                await serialPort.BaseStream.FlushAsync(cancellationTokenSource.Token);

                Debug.WriteLine($"Sent: {message.Command}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Send error: {ex}");
                throw; // Re-throw to be handled by caller
            }
        }

        // Convenience methods for different message types
        public void QueueKeyboardEvent(KeyboardEvent eventType, int keyCode, bool isSystemKey = false)
        {
            byte[] data = new byte[6];

            data[0] = 0x33; data[1] = 0x01;

            data[2] = (byte)(eventType == KeyboardEvent.Down ? 0x01 : 0x00);

            data[3] = (byte)(keyCode);

            data[4] = data[5] = 0x00;

            QueueMessage(data, 1); // Normal priority
        }

        const double scaleX = 17.27, scaleY = 33.25;

        public void QueueMouseEvent(MouseEvent eventType, System.Windows.Point pos, int scroll = 1)
        {
            int x = (int)(pos.X * scaleX), y = (int)(pos.Y * scaleY);
            byte[] data = new byte[6];

            data[1] = (byte)(x & 0xFF);
            data[2] = (byte)((x >> 8) & 0xFF);
            data[3] = (byte)(y & 0xFF);
            data[4] = (byte)((y >> 8) & 0xFF);

            switch (eventType)
            {
                case MouseEvent.Move:
                    data[0] = 0x11;
                    data[5] = 0x11;
                    break;
                case MouseEvent.LDown:
                    data[0] = 0x22;
                    data[5] = 0x00;
                    break;
                case MouseEvent.LUp:
                    data[0] = 0x22;
                    data[5] = 0x01;
                    break;
                case MouseEvent.RDown:
                    data[0] = 0x33;
                    data[5] = 0x00;
                    break;
                case MouseEvent.RUp:
                    data[0] = 0x33;
                    data[5] = 0x01;
                    break;
                case MouseEvent.Scroll:
                    data[0] = 0x44;
                    data[5] = (byte)(scroll > 0 ? 0x01 : 0xFF);
                    break;
            }

            QueueMessage(data, 1, eventType != MouseEvent.Scroll ? 1 : Math.Abs(scroll));
        }

        public void QueueHotkey(string hotkeyType)
        {
            // Todo: implement
        }

        // Clear the queue (emergency use)
        public void ClearQueue()
        {
            while (messageQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref messagesInQueue);
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Disconnect();
                cancellationTokenSource?.Dispose();
                disposed = true;
                Debug.WriteLine("ComPortQueueManager disposed");
            }
        }

        ~ComPortQueueManager()
        {
            Dispose();
        }
    }

    public enum MouseEvent
    {
        Move, LDown, LUp, RDown, RUp, Scroll
    }

    public enum KeyboardEvent
    {
        Down, Up
    }
}