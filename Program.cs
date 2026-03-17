using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Management;

namespace csharp_pc_shutdown_tcp_server_
{
    internal class Program
    {
        private static string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");

        static void Main(string[] args)
        {
            TcpListener server = null;
            Log("Application started...");

            try
            {
                //instantiate tcp server
                Int32 port = 56789;
                server = new TcpListener(IPAddress.Any, port);

                //start listening
                server.Start();

                //buffer for reading data
                byte[] bytes = new byte[256];

                //enter the listening loop
                while (true)
                {
                    Log("Waiting for connection...");
                    using(TcpClient client = server.AcceptTcpClient())
                    {
                        Log($"Connected to client: {client.Client.RemoteEndPoint}");
                        NetworkStream stream = client.GetStream();
                        int i;

                        while((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                        {
                            string data = Encoding.UTF8.GetString(bytes, 0, i).Trim().ToLowerInvariant();
                            Log($"Received command: {data}");

                            if (data.StartsWith("shutdown"))
                            {
                                bool success = ExecuteWmiAction(1, "Shutdown");
                                SendResponse(stream, success ? "Starting_shutdown_fb\x0A" : "Error_shutdown_fb\x0A");
                            }
                            else if (data.StartsWith("reboot"))
                            {
                                bool success = ExecuteWmiAction(2, "Reboot");
                                SendResponse(stream, success ? "Starting_reboot_fb\x0A" : "Error_reboot_fb\x0A");
                            }
                            else if (data.StartsWith("keep_alive"))
                            {
                                SendResponse(stream, "Keep_alive_fb\x0A");
                            }
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log($"CRITICAL SERVER ERROR: {e}");
            }
            finally
            {
                server?.Stop();
                Log("Server stopped.");
            }
        }

        static bool ExecuteWmiAction(int flag, string actionName)
        {
            try
            {
                Log($"Attempting {actionName} via WMI...");
                ConnectionOptions op = new ConnectionOptions { EnablePrivileges = true };
                ManagementScope scope = new ManagementScope(@"\\.\root\cimv2", op);
                scope.Connect();

                ObjectQuery oq = new ObjectQuery("SELECT * FROM Win32_OperatingSystem");
                ManagementObjectSearcher query = new ManagementObjectSearcher(scope, oq);

                foreach (ManagementObject obj in query.Get())
                {
                    // 1 = Shutdown, 2 = Reboot, 5 = Forced Shutdown, 12 = Power Off
                    obj.InvokeMethod("Win32Shutdown", new object[] { flag, 0 });
                }

                Log($"{actionName} command sent successfully.");
                return true;
            }
            catch (Exception e)
            {
                Log($"WMI ERROR during {actionName}: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        static void SendResponse(NetworkStream stream, string message)
        {
            byte[] msg = Encoding.UTF8.GetBytes(message);
            stream.Write(msg, 0, msg.Length);
        }

        static void Log(string message)
        {
            string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            Console.WriteLine(entry);
            try
            {
                File.AppendAllText(logPath, entry + Environment.NewLine);
            }
            catch { /* Avoid crashing if log file is locked */ }
        }
    }
}
