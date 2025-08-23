using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using Baballonia.Contracts;

namespace Baballonia.Helpers
{
    public class SerialCommandSender : ICommandSender
    {
        private const int DefaultBaudRate = 115200; // esptool-rs: Setting baud rate higher than 115,200 can cause issues
        private string port;
        private SerialPort serialPort;

        public SerialCommandSender(string port)
        {
            this.port = port;

            serialPort = new SerialPort(port, DefaultBaudRate);

            // Set serial port parameters
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = Parity.None;
            serialPort.Handshake = Handshake.None;

            // Set read/write timeouts
            serialPort.ReadTimeout = 30000;
            serialPort.WriteTimeout = 30000;
            serialPort.Encoding = Encoding.UTF8;

            serialPort.Open();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();
        }

        public void Dispose()
        {
            if (serialPort.IsOpen)
                serialPort.Close();

            serialPort.Dispose();
        }

        public string ReadLine()
        {
            StringBuilder responseBuilder = new StringBuilder();
            do
            {
                // Read available data
                if (serialPort.BytesToRead > 0)
                {
                    string receivedData = serialPort.ReadLine();
                    responseBuilder.Append(receivedData);
                }
                else
                {
                    // Small delay to prevent CPU spinning
                    Thread.Sleep(10);
                }
            } while (responseBuilder.Length == 0);

            return responseBuilder.ToString().Trim();
        }

        public void WriteLine(string payload)
        {
            serialPort.DiscardInBuffer();

            // Convert the payload to bytes
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

            // Write the payload to the serial port
            const int chunkSize = 64;
            for (int i = 0; i < payloadBytes.Length; i += chunkSize)
            {
                int length = Math.Min(chunkSize, payloadBytes.Length - i);
                serialPort.Write(payloadBytes, i, length);
                Thread.Sleep(50); // Small pause between chunks
            }
            serialPort.Write("\n");

        }
    }
}
