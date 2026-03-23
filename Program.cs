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
        private static bool isLogEnabled = true;
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
            serverTimer.Elapsed += (sender, args) => ĆonnectionTimerElapsed(sender, client, stream);
            clientTimer.AutoReset = false;
            clientTimer.Elapsed += (sender, args) => ĆonnectionTimerElapsed(sender, client, stream);
            
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
                    else if (clientStates.ContainsKey(client.Client.RemoteEndPoint.ToString()) && clientStates[client.Client.RemoteEndPoint.ToString()] == true)
                    {
                        if (clientTimer.Enabled)
                        {
                            clientTimer.Stop();
                        }
                    }

                    byte[] buffer = new byte[1024];
                    Console.WriteLine($"Client {client.Client.RemoteEndPoint} is connected: {client.Connected}");
                    await stream.ReadAsync(buffer, 0, buffer.Length);
                    message = Encoding.UTF8.GetString(buffer);
                    HandleIncomingMessage(client, stream, message);

                    serverTimer.Stop();
                }

                catch (ObjectDisposedException)
                {
                    clientStates.Remove(client.Client.RemoteEndPoint.ToString());
                    client.Close();
                    stream.Close();
                    ChangeState(SerState.Listening);
                    break;
                }
                catch (IOException)
                {
                    clientStates.Remove(client.Client.RemoteEndPoint.ToString());
                    client.Close();
                    stream.Close();
                    ChangeState(SerState.Listening);
                    break;
                }
                catch (SocketException)
                {
                    clientStates.Remove(client.Client.RemoteEndPoint.ToString());
                    client.Close();
                    stream.Close();
                    ChangeState(SerState.Listening);
                    break;
                }
            }

            Console.WriteLine("Client disconnected");
        }

        private static void ĆonnectionTimerElapsed(object sender, TcpClient client, NetworkStream stream)
        {
            if (client.Connected)
            {
                clientStates.Remove(client.Client.RemoteEndPoint.ToString());
                client.Close();
                stream.Close();
                ChangeState(SerState.Listening);
                Console.WriteLine($"[ConnectionTimerElapsed event] current state: {_currentState}");
            }
        }

        private static void HandleIncomingMessage(TcpClient client, NetworkStream stream, string message)
        {
            ChangeState(SerState.Processing);

            if (message.ToLower().StartsWith("shutdown"))
            {
                bool success = false;
                string msg = string.Empty;

                clientStates[client.Client.RemoteEndPoint.ToString()] = true;
                try
                {
                    success = Shutdown(1);
                }
                catch(Exception e)
                {
                    msg = e.ToString();
                }
                SendResponse(stream, success ? "Starting_shutdown_fb\x0A" : $"Error_shutdown_fb: {msg}\x0A");
            }
            else if (message.ToLower().StartsWith("reboot"))
            {
                bool success = false;
                string msg = string.Empty;

                clientStates[client.Client.RemoteEndPoint.ToString()] = true;
                try
                {
                    success = Reboot();
                }
                catch (Exception e)
                {
                    msg = e.ToString();
                }
                SendResponse(stream, success ? "Starting_reboot_fb\x0A" : $"Error_reboot_fb: {msg}\x0A");
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
                SendResponse(stream, "Unknown command.\x0A");
            }

            ChangeState(SerState.Connected);
        }

        static bool Shutdown(int flag)
        {
            if (isLogEnabled)
            {
                Log("Attempting Shutdown via WMI...");
            }

            ConnectionOptions op = new ConnectionOptions { EnablePrivileges = true };
            ManagementScope scope = new ManagementScope(@"\\.\root\cimv2", op);
            scope.Connect();

            ObjectQuery oq = new ObjectQuery("SELECT * FROM Win32_OperatingSystem");
            ManagementObjectSearcher query = new ManagementObjectSearcher(scope, oq);

            foreach(ManagementObject obj in query.Get())
            {
                // 1 = Shutdown, 5 = Forced Shutdown, 12 = Power Off
                obj.InvokeMethod("Win32Shutdown", new object[] { flag, 0 });
            }

            if (isLogEnabled)
            {
                Log("Shutdown command sent successfully");
            }

            return true;
        }

        static bool Reboot()
        {
            if (isLogEnabled)
            {
                Log("Attempting Reboot via WMI...");
            }
            ConnectionOptions op = new ConnectionOptions { EnablePrivileges = true };
            ManagementScope scope = new ManagementScope(@"\\.\root\cimv2", op);
            scope.Connect();

            ObjectQuery oq = new ObjectQuery("SELECT * FROM Win32_OperatingSystem");
            ManagementObjectSearcher query = new ManagementObjectSearcher(scope, oq);

            foreach (ManagementObject obj in query.Get())
            {
                obj.InvokeMethod("Win32Shutdown", new object[] { 2, 0 });
            }

            if (isLogEnabled)
            {
                Log("Reboot command sent successfully.");
            }
            return true;
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
