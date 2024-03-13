using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace ChatServerSide
{

    class Program
    {
        public static void PrintHelpForArg()
        {
            Console.WriteLine("Usage: ChatClient -l [server] -p [port] -d [udpConfirmationTimeout] -r [maxRetransmissions]");
            Console.WriteLine("  -l: Server listening IP address for welcome sockets");
            Console.WriteLine("  -p: Server listening port for welcome sockets");
            Console.WriteLine("  -d: UDP confirmation timeout");
            Console.WriteLine("  -r: Maximum number of UDP retransmissions");
            Console.WriteLine("  -h: Prints program help output and exits");
        }
        static void Main(string[] args)
        {
            int port = 4567;
            string server = "localhost";
            int confirmationTimeout = 250;
            int maxRetransmissions = 3;
            bool pFlag = false, sFlag = false; // Flags to indicate if the mandatory args are set

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-l":
                        server = args[++i];
                        sFlag = true; // Set the flag to true since -l is provided
                        break;
                    case "-p":
                        port = int.Parse(args[++i]);
                        pFlag = true; // Set the flag to true since -p is provided
                        break;
                    case "-d":
                        confirmationTimeout = int.Parse(args[++i]);
                        break;
                    case "-r":
                        maxRetransmissions = int.Parse(args[++i]);
                        break;
                    case "-h":
                        PrintHelpForArg();
                        Environment.Exit(0);
                        return;
                }
            }
            // Check if the mandatory arguments are set
            if (!pFlag || !sFlag)
            {
                Console.WriteLine("ERR: Missing mandatory arguments. -l and -p are required.");
                Environment.Exit(1); // Exit with an error code
            }

            // Create a new instances of the MessageService
            //UdpServer udpServer = new UdpServer(server, port, confirmationTimeout, maxRetransmissions);
            List<User> users = new List<User> { };
            TcpServer tcpServer = new TcpServer(server, port, users);
            UdpServer udpServer = new UdpServer(server, port, users, confirmationTimeout, maxRetransmissions);
            var quitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eArgs) =>
            {
                quitEvent.Set();
                eArgs.Cancel = true;
            };
            var tcpListeningTask = tcpServer.StartListening();
            var udpListeningTask = udpServer.StartListening();
            quitEvent.WaitOne();
        }

        // private static void CancellationHandler(object? sender, ConsoleCancelEventArgs e, bool authorised, CancellationTokenSource cts, MessageService messageService, dynamic listeningTask)
        // {
        //     if (authorised == true)
        //     {
        //         try
        //         {
        //             cts.Cancel();
        //             listeningTask.Wait();
        //             messageService.HandleBye();
        //             messageService.Close();
        //             Environment.Exit(0);
        //             return;
        //         }
        //         catch (Exception ex)
        //         {
        //             Console.WriteLine("ERR: " + ex.Message);
        //         }
        //     }
        // }
    }
}