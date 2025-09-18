using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kingstone.utils
{
    public class ComPortManager
    {
        private SerialPort serialPort;
        private bool isConnected = false;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<bool> ConnectionChanged;

        public bool IsConnected => isConnected;

        public bool Connect(string portName, int baudRate = 115200)
        {
            try
            {
                Disconnect();

                serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    RtsEnable = true,
                    DtrEnable = true
                };

                serialPort.Open();
                isConnected = true;

                StatusChanged?.Invoke(this, $"Connected to {portName}");
                ConnectionChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
                ConnectionChanged?.Invoke(this, false);
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (serialPort?.IsOpen == true)
                {
                    serialPort.Close();
                    serialPort.Dispose();
                }

                isConnected = false;
                StatusChanged?.Invoke(this, "Disconnected");
                ConnectionChanged?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Disconnect error: {ex.Message}");
            }
        }

        public async Task SendCommandAsync(string command)
        {
            if (!isConnected || serialPort?.IsOpen != true)
                return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(command + "\n");
                await serialPort.BaseStream.WriteAsync(data, 0, data.Length);
                await serialPort.BaseStream.FlushAsync(); // Ensure data is sent immediately
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Send error: {ex.Message}");
            }
        }

        // Keep the synchronous version for compatibility, but make it non-blocking
        public void SendCommand(string command)
        {
            // Fire and forget - don't block the UI
            _ = Task.Run(async () => await SendCommandAsync(command));
        }

        public void SendKeyboardEvent(string eventType, int keyCode, bool isSystemKey = false)
        {
            string command = $"KEY|{eventType}|{keyCode}|{(isSystemKey ? 1 : 0)}";
            SendCommand(command);
        }

        public void SendMouseEvent(string eventType, int x, int y, int button = 0, int delta = 0)
        {
            string command = $"MOUSE|{eventType}|{x}|{y}|{button}|{delta}";
            SendCommand(command);
        }

        public void SendHotkey(string hotkeyType)
        {
            string command = $"HOTKEY|{hotkeyType}";
            SendCommand(command);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
