using System;
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
        static void Main(string[] args)
        {
            TcpListener server = null;
            try
            {
                //instantiate tcp server
                Int32 port = 6789;
                //IPAddress localAddr = IPAddress.Parse("127.0.0.1");
                server = new TcpListener(IPAddress.Any, port);

                //start listening
                server.Start();

                //buffer for reading data
                byte[] bytes = new byte[256];
                string data = null;

                //enter the listening loop
                while (true)
                {
                    Console.WriteLine("Waiting for connection...");

                    //perform a blocking call to accept requests
                    TcpClient client = server.AcceptTcpClient();
                    Console.WriteLine("Connected!");

                    data = null;

                    //get a stream object for reading and writing
                    NetworkStream stream = client.GetStream();

                    int i;

                    //loop to receive all the data sent by the client
                    while((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        //translate data bytes to UTF8 string
                        data = Encoding.UTF8.GetString(bytes, 0, i).Trim().ToLowerInvariant();
                        Console.WriteLine("Received: {0}", data);

                        if (data.StartsWith("shutdown"))
                        {
                            ShutdownPc();
                            byte[] dataToSend = Encoding.UTF8.GetBytes("Starting_shutdown_fb\x0A");
                            stream.Write(dataToSend, 0, dataToSend.Length);
                        }
                        else if (data.ToLower().StartsWith("keep_alive"))
                        {
                            byte[] dataToSend = Encoding.UTF8.GetBytes("Keep_Alive_fb\x0A");
                            stream.Write(dataToSend, 0, dataToSend.Length);
                        }
                    }
                    client.Close();
                }
            }
            catch(SocketException e)
            {
                Console.WriteLine("Socket exception: {0}", e.ToString());
            }
            finally
            {
                if(server != null)
                {
                    server.Stop();

                }
            }

            Console.WriteLine("Hit enter to continue");
            Console.ReadKey();


        }

        static void ShutdownPc()
        {
            Console.WriteLine("Beginning of PCShutdown method");
            ConnectionOptions op = new ConnectionOptions();
            op.EnablePrivileges = true;

            // Make a connection to a remote computer.  
            Console.WriteLine("PCShutdown method Make a connection to a remote computer");
            ManagementScope scope = new ManagementScope("\\root\\cimv2", op);
            scope.Connect();

            Console.WriteLine("PCShutdown method connected to the remote computer: {0}", scope.IsConnected.ToString());


            //Query system for Operating System information  
            Console.WriteLine("PCShutdown method Query system for Operating System information");
            ObjectQuery oq = new ObjectQuery("SELECT * FROM Win32_OperatingSystem");

            Console.WriteLine("PCShutdown method invoke shutdown method");
            ManagementObjectSearcher query = new ManagementObjectSearcher(scope, oq);
            ManagementObjectCollection queryCollection = query.Get();
            foreach (ManagementObject obj in queryCollection)
            {
                obj.InvokeMethod("ShutDown", null); //shutdown  
            }
        }
    }
}
