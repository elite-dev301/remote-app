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
        private Task jiggleTask;
        private bool isConnected = false;
        private bool disposed = false;

        // Add these fields for jiggling
        private DateTime lastMessageTime = DateTime.Now;
        private readonly TimeSpan baseJiggleInterval = TimeSpan.FromMinutes(5);
        private readonly object jiggleLock = new object();
        private readonly Random random = new Random();

        // Configurable jiggle settings
        private readonly int minJiggleSeconds = 120; // 4.5 minutes
        private readonly int maxJiggleSeconds = 180; // 5.5 minutes
        private readonly int minMouseX = 0x1000;
        private readonly int maxMouseX = 0x4000;
        private readonly int minMouseY = 0x1000;
        private readonly int maxMouseY = 0x4000;

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

                // Start jiggle monitor task
                jiggleTask = Task.Run(MonitorAndJiggle, cancellationTokenSource.Token);

                StatusChanged?.Invoke(this, $"Connected to {portName}");
                ConnectionChanged?.Invoke(this, true);
                Console.WriteLine($"COM port connected: {portName}");

                return true;
            }
            catch (Exception ex)
            {
                string errorMsg = $"Connection failed: {ex.Message}";
                StatusChanged?.Invoke(this, errorMsg);
                ConnectionChanged?.Invoke(this, false);
                Console.WriteLine($"COM port connection error: {ex}");
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
                jiggleTask?.Wait(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Disconnect error: {ex}");
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
                Console.WriteLine("COM port disconnected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Disconnect error: {ex}");
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

                for (int i = 0; i < count; i++)
                {
                    messageQueue.Enqueue(message);
                    Interlocked.Increment(ref messagesInQueue);
                    Interlocked.Increment(ref totalMessagesQueued);
                }

                lock (jiggleLock)
                {
                    if (command[0] == 0x11) lastMessageTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error queuing message: {ex}");
            }
        }

        // New jiggle monitoring task with random intervals
        private async Task MonitorAndJiggle()
        {
            Console.WriteLine("Mouse jiggle monitor started");

            // Generate initial random interval
            int nextJiggleSeconds = GetRandomJiggleInterval();
            Console.WriteLine($"Next jiggle scheduled in {nextJiggleSeconds} seconds");

            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (!isConnected || serialPort?.IsOpen == false)
                    {
                        await Task.Delay(1000, cancellationTokenSource.Token);
                        continue;
                    }

                    DateTime lastTime;
                    lock (jiggleLock)
                    {
                        lastTime = lastMessageTime;
                    }

                    TimeSpan elapsed = DateTime.Now - lastTime;

                    // Console.WriteLine($"No activity for {elapsed.TotalSeconds} seconds - plan to excute mouse jiggle {nextJiggleSeconds}");

                    if (elapsed >= TimeSpan.FromSeconds(nextJiggleSeconds))
                    {
                        // Generate random mouse positions
                        var point1 = GetRandomMousePosition();
                        var point2 = GetRandomMousePosition();

                        Console.WriteLine($"No activity for {nextJiggleSeconds} seconds - executing mouse jiggle");
                        Console.WriteLine($"Moving to ({point1.X}, {point1.Y}) then ({point2.X}, {point2.Y})");

                        // Queue jiggle movements
                        QueueMouseEvent(MouseEvent.Move, point1);
                        await Task.Delay(100, cancellationTokenSource.Token);
                        QueueMouseEvent(MouseEvent.Move, point2);

                        // Reset timer and generate new random interval
                        lock (jiggleLock)
                        {
                            lastMessageTime = DateTime.Now;
                        }

                        nextJiggleSeconds = GetRandomJiggleInterval();
                        Console.WriteLine($"Next jiggle scheduled in {nextJiggleSeconds} seconds");
                    }

                    // Check every 10 seconds
                    await Task.Delay(10000, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Jiggle monitor error: {ex}");
                    await Task.Delay(1000, cancellationTokenSource.Token);
                }
            }

            Console.WriteLine("Mouse jiggle monitor stopped");
        }

        // Generate random jiggle interval
        private int GetRandomJiggleInterval()
        {
            lock (jiggleLock)
            {
                return random.Next(minJiggleSeconds, maxJiggleSeconds + 1);
            }
        }

        // Generate random mouse position
        private System.Windows.Point GetRandomMousePosition()
        {
            lock (jiggleLock)
            {
                int x = random.Next(minMouseX, maxMouseX + 1);
                int y = random.Next(minMouseY, maxMouseY + 1);
                return new System.Windows.Point(x, y);
            }
        }

        // Background worker thread
        private async Task ProcessMessageQueue()
        {
            Console.WriteLine("COM port message queue worker started");

            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (!isConnected || serialPort?.IsOpen == false) continue;

                    ComPortMessage mouseMessage = null, lastMessage = null;

                    while (messageQueue.TryDequeue(out lastMessage) && lastMessage.Command[0] == 0x11)
                    {
                        Interlocked.Decrement(ref messagesInQueue);
                        mouseMessage = lastMessage;
                    }

                    if (mouseMessage != null || lastMessage != null)
                    {
                        ComPortMessage[] messages = new ComPortMessage[] { mouseMessage, lastMessage };

                        foreach (ComPortMessage message in messages)
                        {

                            if (message == null) continue;

                            // Send the message
                            await SendMessageAsync(message);

                            if (message.Command[0] != 0x11) Interlocked.Decrement(ref messagesInQueue);
                            Interlocked.Increment(ref totalMessagesSent);

                            // Notify message sent
                            MessageSent?.Invoke(this, message);

                            // Small delay to prevent overwhelming the receiving device
                            await Task.Delay(1, cancellationTokenSource.Token);
                        }
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
                    Console.WriteLine($"Message queue worker error: {ex}");
                    SendError?.Invoke(this, ex);

                    // Brief delay on error to prevent tight error loops
                    await Task.Delay(100, cancellationTokenSource.Token);
                }
            }

            Console.WriteLine("COM port message queue worker stopped");
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send error: {ex}");
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

        public void QueueMouseEvent(MouseEvent eventType, System.Windows.Point pos, int scroll = 1)
        {
            int x = (int)(pos.X), y = (int)(pos.Y);
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
                    data[0] = 0x22;
                    data[5] = 0x02;
                    break;
                case MouseEvent.RUp:
                    data[0] = 0x22;
                    data[5] = 0x03;
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

            if (hotkeyType == "CtrlAltDel")
            {
                QueueKeyboardEvent(KeyboardEvent.Down, 0x84);
                QueueKeyboardEvent(KeyboardEvent.Down, 0x86);
                QueueKeyboardEvent(KeyboardEvent.Down, 0xD4);

                QueueKeyboardEvent(KeyboardEvent.Up, 0x84);
                QueueKeyboardEvent(KeyboardEvent.Up, 0x86);
                QueueKeyboardEvent(KeyboardEvent.Up, 0xD4);
            } else if (hotkeyType == "PrintScreen")
            {
                QueueKeyboardEvent(KeyboardEvent.Down, 0xCE);
                QueueKeyboardEvent(KeyboardEvent.Up, 0xCE);
            }
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
                Console.WriteLine("ComPortQueueManager disposed");
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