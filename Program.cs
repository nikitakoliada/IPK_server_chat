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
            bool pts = false;
            TcpServer tcpServer = new TcpServer(server, port,  ref users);
            UdpServer udpServer = new UdpServer(server, port, ref users, confirmationTimeout, maxRetransmissions);
            CancellationTokenSource cts = new CancellationTokenSource();
            var quitEvent = new ManualResetEvent(false);

            Console.CancelKeyPress += (sender, eArgs) =>
            {
                quitEvent.Set();
                eArgs.Cancel = true;
            };
            var tcpListeningTask = tcpServer.StartListening(cts);
            var udpListeningTask = udpServer.StartListening(cts);
            Console.CancelKeyPress += new ConsoleCancelEventHandler((sender, e) => CancellationHandler(sender, e, tcpListeningTask, udpListeningTask, cts));
            quitEvent.WaitOne();
        }

        private static void CancellationHandler(object? sender, ConsoleCancelEventArgs e, dynamic tcpListeningTask, dynamic udpListeningTask , CancellationTokenSource cts)
        {

            try
            {
                cts.Cancel();
                
                tcpListeningTask.Wait();
                udpListeningTask.Wait();
                Environment.Exit(0);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR: " + ex.Message);
            }
        }

    }
}