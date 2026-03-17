using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace csharp_pc_shutdown_tcp_server_
{
    internal class Program
    {
        private static string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
        private static TcpListener server;
        private const int PORT = 56789;
        private static bool isLogEnabled = false;

        static void Main(string[] args)
        {
            // instantiate tcp server
            server = new TcpListener(IPAddress.Any, PORT);
            server.Start();

            Console.WriteLine($"Server started listening on port {PORT}.");
            if (isLogEnabled)
            {
                Log($"Server started listening on port {PORT}.");
            }

            // blocking call because Main() method is synchronous
            Task listener = HandleListenerAsync(server);
            listener.GetAwaiter().GetResult();
        }

        private static async Task HandleListenerAsync(TcpListener server)
        {
            // server is in the listening state
            while (true)
            {
                // If a client is connected, a new socket is created, which is in the accepted state and can exchange information 
                using (var client = await server.AcceptTcpClientAsync())
                {
                    Console.WriteLine($"Connected to client: {client.Client.RemoteEndPoint}");
                    if (isLogEnabled)
                    {
                        Log($"Connected to client: {client.Client.RemoteEndPoint}");
                    }
                    await HandleClientAsync(client);
                }
                    
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            //Send and receive information from client [bytes]
            NetworkStream stream = client.GetStream();
            Timer timer = new Timer(10000);
            timer.AutoReset = false;
            timer.Elapsed += (sender, args) => KeepAliveElapsed(sender, client);

            string message;

            while (true)
            {
                try
                {
                    timer.Start();
                    byte[] buffer = new byte[1024];
                    await stream.ReadAsync(buffer, 0, buffer.Length);

                    message = Encoding.UTF8.GetString(buffer);
                    HandleIncomingMessage(stream, message);
                    timer.Stop();
                }
                catch(ObjectDisposedException)
                {
                    break;
                }
            }

            Console.WriteLine("Client disconnected");
        }

        private static void KeepAliveElapsed(object sender, TcpClient client)
        {
            Console.WriteLine($"Keep alive timer for {client.Client.RemoteEndPoint} elapsed");
            client.Close();
        }

        private static void HandleIncomingMessage(NetworkStream stream, string message)
        {
            if (message.ToLower().StartsWith("shutdown"))
            {
                //bool success = ExecuteWmiAction(1, "Shutdown");
                bool success = true;
                SendResponse(stream, success ? "Starting_shutdown_fb\x0A" : "Error_shutdown_fb\x0A");
            }
            else if (message.ToLower().StartsWith("reboot"))
            {
                //bool success = ExecuteWmiAction(2, "Reboot");
                bool success = true;
                SendResponse(stream, success ? "Starting_reboot_fb\x0A" : "Error_reboot_fb\x0A");
            }
            else if (message.ToLower().StartsWith("keep_alive"))
            {
                SendResponse(stream, "Keep_alive_fb\x0A");
            }
            else if (message.ToLower().StartsWith("log_enable"))
            {
                isLogEnabled = true;
                SendResponse(stream, "Log enabled.\x0A");
            }
            else if (message.ToLower().StartsWith("log_disable"))
            {
                isLogEnabled = false;
                SendResponse(stream, "Log disabled.\x0A");
            }
        }

        static bool ExecuteWmiAction(int flag, string actionName)
        {
            try
            {
                if (isLogEnabled)
                {
                    Log($"Attempting {actionName} via WMI...");
                }
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

                if (isLogEnabled)
                {
                    Log($"{actionName} command sent successfully.");
                }
                return true;
            }
            catch (Exception e)
            {
                if (isLogEnabled)
                {
                    Log($"WMI ERROR during {actionName}: {e.Message}\n{e.StackTrace}");
                }
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
