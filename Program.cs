using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
        private static Dictionary<string, bool> clientStates;

        public enum SerState
        {
            Stopped,    // Serwer nie pracuje
            Listening,  // Serwer nasłuchuje
            Connected,  // Jest połączenie
            Processing, // Słucha komend
            TimedOut,   // Utrzymanie połączenia ma timeout
            Faulted     // Błąd
        }

        private static SerState _currentState = SerState.Stopped;

        static void Main(string[] args)
        {
            clientStates = new Dictionary<string, bool>();
            // instantiate tcp server
            server = new TcpListener(IPAddress.Any, PORT);
            server.Start();
            ChangeState(SerState.Listening);

            Console.WriteLine($"Server started listening on port {PORT}.");
            if (isLogEnabled)
            {
                Log($"Server started listening on port {PORT}.");
            }

            // blocking call because Main() method is synchronous
            Task listener = HandleListenerAsync(server);
            listener.GetAwaiter().GetResult();
        }

        private static void ChangeState (SerState newState)
        {
            if(_currentState == newState) return;
            _currentState = newState;
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
                    clientStates.Add(client.Client.RemoteEndPoint.ToString(), true);
                    ChangeState(SerState.Connected);
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
            Timer serverTimer = new Timer(60000);
            Timer clientTimer = new Timer(10000);
            serverTimer.AutoReset = false;
            serverTimer.Elapsed += (sender, args) => KeepAliveElapsed(sender, client, stream);
            clientTimer.AutoReset = false;
            clientTimer.Elapsed += (sender, args) => ClientConnectedElapsed(sender, client, stream);
            
            string message;

            while (true)
            {
                try
                {
                    serverTimer.Start();
                    if (clientStates.ContainsKey(client.Client.RemoteEndPoint.ToString()) && clientStates[client.Client.RemoteEndPoint.ToString()] == false)
                    {
                        if (!clientTimer.Enabled)
                        {
                            clientTimer.Start();
                        }
                    }
                    else if(clientStates.ContainsKey(client.Client.RemoteEndPoint.ToString()) && clientStates[client.Client.RemoteEndPoint.ToString()] == true)
                    {
                        if (clientTimer.Enabled)
                        {
                            clientTimer.Stop();
                        }
                    }

                    byte[] buffer = new byte[1024];
                    await stream.ReadAsync(buffer, 0, buffer.Length);
                    message = Encoding.UTF8.GetString(buffer);
                    HandleIncomingMessage(client, stream, message);
                    
                    serverTimer.Stop();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }

            Console.WriteLine("Client disconnected");
        }

        private static void KeepAliveElapsed(object sender, TcpClient client, NetworkStream stream)
        {
            if (client.Connected)
            {
                client.Close();
                stream.Close();
            }
        }

        private static void ClientConnectedElapsed(object sender, TcpClient client, NetworkStream stream)
        {
            if (client.Connected)
            {
                clientStates.Remove(client.Client.RemoteEndPoint.ToString());
                client.Close();
                stream.Close();
            }
        }

        private static void HandleIncomingMessage(TcpClient client, NetworkStream stream, string message)
        {
            if (message.ToLower().StartsWith("shutdown"))
            {
                clientStates[client.Client.RemoteEndPoint.ToString()] = true;
                //bool success = ExecuteWmiAction(1, "Shutdown");
                bool success = true;
                SendResponse(stream, success ? "Starting_shutdown_fb\x0A" : "Error_shutdown_fb\x0A");
            }
            else if (message.ToLower().StartsWith("reboot"))
            {
                clientStates[client.Client.RemoteEndPoint.ToString()] = true;
                //bool success = ExecuteWmiAction(2, "Reboot");
                bool success = true;
                SendResponse(stream, success ? "Starting_reboot_fb\x0A" : "Error_reboot_fb\x0A");
            }
            else if (message.ToLower().StartsWith("keep_alive"))
            {
                clientStates[client.Client.RemoteEndPoint.ToString()] = true;
                SendResponse(stream, "Keep_alive_fb\x0A");
            }
            else if (message.ToLower().StartsWith("log_enable"))
            {
                isLogEnabled = true;
                clientStates[client.Client.RemoteEndPoint.ToString()] = true;
                SendResponse(stream, "Log enabled.\x0A");
            }
            else if (message.ToLower().StartsWith("log_disable"))
            {
                isLogEnabled = false;
                clientStates[client.Client.RemoteEndPoint.ToString()] = true;
                SendResponse(stream, "Log disabled.\x0A");
            }
            else
            {
                clientStates[client.Client.RemoteEndPoint.ToString()] = false;
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
