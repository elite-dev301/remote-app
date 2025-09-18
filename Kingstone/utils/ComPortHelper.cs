using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Linq;

public class ComPortInfo
{
    public string PortName { get; set; }
    public string Description { get; set; }
    public string DeviceID { get; set; }
    public bool IsAvailable { get; set; }

    public override string ToString()
    {
        return Description;
    }
}

public static class ComPortHelper
{
    public static List<ComPortInfo> GetComPorts()
    {
        var comPorts = new List<ComPortInfo>();

        try
        {
            // Get available port names
            var availablePorts = SerialPort.GetPortNames().ToList();

            // Query WMI for detailed information
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string caption = obj["Caption"]?.ToString();
                    string deviceID = obj["DeviceID"]?.ToString();

                    if (!string.IsNullOrEmpty(caption))
                    {
                        // Extract COM port number from caption
                        var match = System.Text.RegularExpressions.Regex.Match(caption, @"\(COM(\d+)\)");
                        if (match.Success)
                        {
                            string portName = $"COM{match.Groups[1].Value}";
                            bool isAvailable = availablePorts.Contains(portName);

                            comPorts.Add(new ComPortInfo
                            {
                                PortName = portName,
                                Description = caption,
                                DeviceID = deviceID,
                                IsAvailable = isAvailable
                            });
                        }
                    }
                }
            }

            // Add any ports that weren't found in WMI
            foreach (var port in availablePorts)
            {
                if (!comPorts.Any(cp => cp.PortName == port))
                {
                    comPorts.Add(new ComPortInfo
                    {
                        PortName = port,
                        Description = $"Communications Port ({port})",
                        DeviceID = "Unknown",
                        IsAvailable = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting COM ports: {ex.Message}");
        }

        return comPorts.OrderBy(cp => cp.PortName).ToList();
    }

    public static List<ComPortInfo> GetAvailableComPorts()
    {
        return GetComPorts().Where(cp => cp.IsAvailable).ToList();
    }
}