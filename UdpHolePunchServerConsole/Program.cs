using System;
using System.Linq;
using System.Timers;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Networking;
using Networking.Messages;
using UdpHolePunchServerConsole.Models;

namespace UdpHolePunchServerConsole
{
    class Program
    {
        private static Server _server;
        private static Clients _clients;
        private static Timer _keepAliveTimer;

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            _clients = new Clients();
            _clients.ClientAdded += ClientAddedInCollection;
            _clients.ClientRemoved += ClientRemovedFromCollection;

            _server = new Server();
            _server.ClientAdded += ClientAdded;
            _server.ClientRemoved += ClientRemoved;
            _server.MessageReceived += MessageReceived;

            _keepAliveTimer = new Timer();
            _keepAliveTimer.Interval = 5000;
            _keepAliveTimer.Elapsed += OnKeepAliveTimerTick;
            _keepAliveTimer.Start();

            var port = 0;
            if (args.Length == 0)
            {
                port = AskPort();
                Console.WriteLine($"Server will listen on port {port}");
            }
            else
            if (args.Length == 1)
            {
                if (int.TryParse(args[0], out port) &&
                    port > 1024 &&
                    port < 65536 &&
                    !IsPortOccupied(port))
                {
                    Console.WriteLine($"Server will listen on port {port}");
                }
                else
                {
                    Console.WriteLine($"Can't listen on port {args[0]}, shutting down...");

                    Environment.Exit(0);
                }
            }
            else
            if (args.Length > 1)
            {
                Console.WriteLine("Invalid number of parameters");

                Environment.Exit(0);
            }

            await _server.StartListening(port);
        }

        private static void OnExit(object sender, EventArgs e)
        {
            if (_server != null)
            {
                _server.DisconnectAll();
            }
        }

        private static int AskPort()
        {
            var port = 0;
            var isFreePortChosen = true;
            var defaultPort = 56000;

            do
            {
                Console.WriteLine("Enter port number of Chat Tracker:");
                if (int.TryParse(Console.ReadLine(), out var portNumber) &&
                    portNumber > 1024 &&
                    portNumber < 65536)
                {
                    if (!IsPortOccupied(portNumber))
                    {
                        isFreePortChosen = true;
                        port = portNumber;
                    }
                    else
                    {
                        Console.WriteLine($"Port {portNumber} is already occupied! Use port from range [1025; 65535]");
                    }
                }
                else
                {
                    if (!IsPortOccupied(defaultPort))
                    {
                        isFreePortChosen = true;
                        port = defaultPort;
                    }
                    else
                    {
                        Console.WriteLine("Default port is already occupied, appliction will be shut down!");

                        Environment.Exit(0);
                    }
                }
            }
            while (!isFreePortChosen);

            return port;
        }

        private static void ClientAddedInCollection(object sender, EventArgs e)
        {
            Console.WriteLine("(ClientAddedInCollection)");
        }

        private static void ClientRemovedFromCollection(object sender, EventArgs e)
        {
            Console.WriteLine("(ClientRemovedFromCollection)");
        }

        private static void ClientAdded(object sender, EncryptedPeerEventArgs e)
        {
            Console.WriteLine("Client added: " + e.Peer.EndPoint);
        }

        private static void ClientRemoved(object sender, EncryptedPeerEventArgs e)
        {
            Console.WriteLine("Client removed: " + e.Peer.EndPoint);

            var user = _clients.Get(e.Peer);
            if (user == null)
            {
                return;
            }

            _clients.Remove(user);
        }

        private static void MessageReceived(object sender, NetEventArgs e)
        {
            var type = e.Type;
            var json = e.Json;
            var source = e.EncryptedPeer;

            Console.WriteLine("(MessageReceived) source = " + source.EndPoint);
            Console.WriteLine("(MessageReceived) type = " + type);

            var sourceClient = _clients.Get(source);

            switch (type)
            {
                case NetworkMessageType.KeepAlive:
                    Console.WriteLine($"KeepAliveMessage from {source.EndPoint}");
                    break;

                case NetworkMessageType.IntroduceClientToTracker:
                    var clientIntroduceMessage = JsonConvert.DeserializeObject<IntroduceClientToTrackerMessage>(json);
                    if (clientIntroduceMessage == null)
                    {
                        return;
                    }

                    if (_clients.Has(clientIntroduceMessage.ID))
                    {
                        var errorMessage = new IntroduceClientToTrackerErrorMessage();
                        source.SendEncrypted(errorMessage);

                        return;
                    }

                    var client = new ClientModel(source, clientIntroduceMessage.ID, clientIntroduceMessage.Nickname);
                    _clients.Add(client);

                    client.SendIntroductionResponseMessage();

                    Console.WriteLine($"User {client.EndPoint} ({client.ID}) has connected");
                    break;

                case NetworkMessageType.UpdatedInfoForTracker:
                    if (sourceClient == null)
                    {
                        return;
                    }

                    var updatedInfoMessage = JsonConvert.DeserializeObject<UpdatedInfoToTrackerMessage>(json);
                    if (updatedInfoMessage == null)
                    {
                        return;
                    }

                    sourceClient.UpdateNickname(updatedInfoMessage.NewNickname);
                    break;

                case NetworkMessageType.CommandToTracker:
                    if (sourceClient == null)
                    {
                        return;
                    }

                    var commandMessage = JsonConvert.DeserializeObject<CommandToTrackerMessage>(json);
                    if (commandMessage == null)
                    {
                        return;
                    }

                    sourceClient.IncreaseMessageCount();
                    sourceClient.SendCommandReceiptMessage(commandMessage.CommandID);
                    ParseCommand(sourceClient, commandMessage);
                    break;
            }
        }

        private static void ParseCommand(ClientModel sourceClient, CommandToTrackerMessage commandMessage)
        {
            var command = commandMessage.Command.ToLower();
            var argument = commandMessage.Argument;

            switch (command)
            {
                case "connect":
                    var desiredClient = _clients.GetByID(argument);
                    if (desiredClient != null)
                    {
                        sourceClient.SendConnectionResponseMessage(desiredClient.ID, desiredClient.EndPoint);
                        desiredClient.SendForwardedConnectionRequestMessage(sourceClient.ID, sourceClient.EndPoint);

                        break;
                    }

                    var clientsWithSpecifiedNickname = _clients.GetByNickname(argument);
                    if (clientsWithSpecifiedNickname.Length > 0)
                    {
                        sourceClient.SendListOfUsersWithSpecifiedNickname(clientsWithSpecifiedNickname);

                        break;
                    }

                    sourceClient.SendUserNotFoundErrorMessage(argument);
                    break;

                case "ping":
                    sourceClient.SendPingResponseCommand();
                    break;

                case "time":
                    sourceClient.SendTimeResponseCommand();
                    break;

                default:
                    sourceClient.SendCommandErrorMessage(commandMessage.Command, commandMessage.Argument);
                    break;
            }
        }

        private static void OnKeepAliveTimerTick(object sender, ElapsedEventArgs e)
        {
            _server.SendToAll(new KeepAliveMessage());
        }

        private static bool IsPortOccupied(int port)
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(p => p.Port == port);
        }
    }
}
