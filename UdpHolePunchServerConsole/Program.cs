using System;
using Newtonsoft.Json;
using NetworkingLib;
using NetworkingLib.Messages;
using Extensions;
using UdpHolePunchServerConsole.Models;

namespace UdpHolePunchServerConsole
{
    class Program
    {
        private static Server _server;
        private static bool _canStart;
        private static Clients _clients;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            _clients = new Clients();
            _clients.ClientAdded += ClientAddedInCollection;
            _clients.ClientRemoved += ClientRemovedFromCollection;

            _server = new Server();
            _server.ClientAdded += ClientAdded;
            _server.ClientRemoved += ClientRemoved;
            _server.MessageFromClientReceived += MessageReceived;

            _canStart = false;

            var port = 0;
            if (args.Length == 0)
            {
                port = AskPort();
                Console.WriteLine($"Tracker will listen on port {port}");
            }
            else
            if (args.Length == 1)
            {
                if (int.TryParse(args[0], out port) &&
                    port > 1024 &&
                    port < 65536 &&
                    !port.IsPortOccupied())
                {
                    _canStart = true;
                    Console.WriteLine($"Tracker will listen on port {port}");
                }
                else
                {
                    Console.WriteLine($"Can't listen on port {args[0]}, shutting the tracker down...");
                }
            }
            else
            if (args.Length != 1)
            {
                Console.WriteLine("Invalid number of parameters");
            }

            if (_canStart)
            {
                _server.StartListening(port);

                while (true)
                {
                    Console.WriteLine("Enter 'quit' to stop the tracker");
                    Console.WriteLine("Enter 'info' to print current parameters of the tracker");
                    var line = Console.ReadLine();
                    var input = line.ToLower();
                    
                    if (input == "quit")
                    {
                        _server.Stop();

                        break;
                    }
                    else
                    if (input == "info")
                    {
                        Console.WriteLine($"Port: {_server.LocalPort}, current users number: {_clients.Count}");
                        Console.WriteLine();
                    }
                }
            }
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
                    if (!portNumber.IsPortOccupied())
                    {
                        isFreePortChosen = true;
                        port = portNumber;
                        _canStart = true;
                    }
                    else
                    {
                        Console.WriteLine($"Port {portNumber} is already occupied! Use port from range [1025; 65535]");
                    }
                }
                else
                {
                    if (!defaultPort.IsPortOccupied())
                    {
                        isFreePortChosen = true;
                        port = defaultPort;
                        _canStart = true;
                    }
                    else
                    {
                        Console.WriteLine($"Default port {defaultPort} is already occupied, appliction will be shut down!");

                        break;
                    }
                }
            }
            while (!isFreePortChosen);

            return port;
        }

        private static void ClientAddedInCollection(object sender, EventArgs e)
        {
            Console.WriteLine($"(ClientAddedInCollection) {DateTime.Now.ConvertTime()}");
        }

        private static void ClientRemovedFromCollection(object sender, EventArgs e)
        {
            Console.WriteLine($"(ClientRemovedFromCollection) {DateTime.Now.ConvertTime()}");
        }

        private static void ClientAdded(object sender, EncryptedPeerEventArgs e)
        {
            Console.WriteLine($"{DateTime.Now.ConvertTime()} Client added: {e.EndPoint}");
        }

        private static void ClientRemoved(object sender, EncryptedPeerEventArgs e)
        {
            Console.WriteLine($"{DateTime.Now.ConvertTime()} Client removed: {e.EndPoint}");

            var user = _clients.GetByAddress(e.EndPoint);
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

            Console.WriteLine($"{DateTime.Now.ConvertTime()} (MessageReceived) Source = {source.EndPoint}, type = {type}");

            var sourceClient = _clients.GetByAddress(source.EndPoint);

            switch (type)
            {
                case NetworkMessageType.IntroduceClientToTracker:
                    var clientIntroduceMessage = JsonConvert.DeserializeObject<IntroduceClientToTrackerMessage>(json);
                    if (clientIntroduceMessage == null)
                    {
                        return;
                    }

                    if (_clients.Has(clientIntroduceMessage.ID))
                    {
                        var errorMessage = new IntroduceClientToTrackerErrorMessage();
                        source.SendEncrypted(errorMessage, 0);

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
                    var desiredClient = _clients.GetByUserID(argument);
                    if (desiredClient != null)
                    {
                        sourceClient.SendConnectionResponseMessage(desiredClient.ID, desiredClient.EndPoint);
                        desiredClient.SendForwardedConnectionRequestMessage(sourceClient.ID, sourceClient.EndPoint);

                        break;
                    }

                    var clientsWithSpecifiedNickname = _clients.GetByNickname(argument);
                    if (clientsWithSpecifiedNickname.Count > 0)
                    {
                        sourceClient.SendListOfUsersWithSpecifiedNickname(argument, clientsWithSpecifiedNickname);

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
    }
}
