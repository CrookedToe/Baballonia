using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.Json;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services.Firmware;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class FirmwareService(ILogger<FirmwareService> logger, ICommandSenderFactory commandSenderFactory)
{
    public event Action OnFirmwareUpdateStart;
    public event Action OnFirmwareUpdateComplete;
    public event Action<string> OnFirmwareUpdateError;

    private static readonly string EsptoolCommand;

    private const int DefaultBaudRate = 921600; // esptool-rs: Setting baud rate higher than 115,200 can cause issues

    static FirmwareService()
    {
        EsptoolCommand = OperatingSystem.IsWindows() ? "espflash.exe" : "espflash";
    }

    public void UploadFirmware(string port, string pathToFirmware)
    {
        try
        {
            // Check if firmware file exists
            if (!File.Exists(pathToFirmware))
            {
                OnFirmwareUpdateError($"Firmware file not found: {pathToFirmware}");
                return;
            }

            // Notify start of firmware update
            OnFirmwareUpdateStart();

            // Create process to run esptool.py
            if (!RunEspSubprocess(
                    arguments:
                    $"write-bin 0x00 \"{pathToFirmware}\" --port {port} --baud {DefaultBaudRate}"))
            {
                OnFirmwareUpdateError($"Firmware update failed!");
            }

            // Wired firmware update completed successfully
            OnFirmwareUpdateComplete();
        }
        catch (Exception ex)
        {
            OnFirmwareUpdateError?.Invoke($"Firmware update failed: {ex.Message}");
        }
    }

    private bool RunEspSubprocess(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = EsptoolCommand;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            OnFirmwareUpdateError($"Firmware update failed: {ex.Message}");
            return false;
        }
    }

    public FirmwareSession StartSession(CommandSenderType type, string port)
    {
        return new FirmwareSession(commandSenderFactory.Create(type, port), logger);
    }

    // Tests if a COM port has a Babble heartbeat
    public string[] ProbeComPorts(TimeSpan timeout)
    {
        var ports = FindAvailableComPorts();
        List<string> goodPorts = [];
        foreach (var port in ports)
        {
            var session = StartSession(CommandSenderType.Serial, port);
            try
            {
                logger.LogInformation("Probing {}", port);
                var heartbeat = session.WaitForHeartbeat(timeout);
                if (heartbeat != null)
                {
                    goodPorts.Add(port);
                }

                session.Dispose();
            }
            catch (FileNotFoundException ex)
            {
                logger.LogInformation("probing port {}: could not probe", port);
            }
            catch (TimeoutException ex)
            {
                logger.LogInformation("probing port {}: timeout reached", port);
            }
            catch (Exception ex)
            {
                logger.LogError("Error probing port {}: {}", port, ex.Message);
            }
            finally
            {
                session.Dispose();
            }
        }

        return [.. goodPorts];
    }

    public string[] FindAvailableComPorts()
    {
        // GetPortNames() may return single port multiple times
        // https://stackoverflow.com/questions/33401217/serialport-getportnames-returns-same-port-multiple-times
        return SerialPort.GetPortNames().Distinct().ToArray();
    }
}

