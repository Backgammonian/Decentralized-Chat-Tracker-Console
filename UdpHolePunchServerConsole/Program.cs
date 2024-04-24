using System;
using System.Timers;
using System.Threading.Tasks;
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
        private static Clients _clients;
        private static Timer _logTimer;

        static async Task Main()
        {
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            _clients = new Clients();
            _clients.ClientAdded += ClientAddedInCollection;
            _clients.ClientRemoved += ClientRemovedFromCollection;

            _server = new Server();
            _server.ClientAdded += ClientAdded;
            _server.ClientRemoved += ClientRemoved;
            _server.MessageFromClientReceived += MessageReceived;

            _logTimer = new Timer();
            _logTimer.Interval = 5000;
            _logTimer.Elapsed += OnLogTimerElapsed;
            _logTimer.Start();

            var portNumber = 65000;
            PrintLog();
            //await _server.StartListeningAsync(portNumber);
            _server.StartListening(portNumber);

            while (true)
            {
               await Task.Delay(50);
            }
        }

        private static void OnExit(object sender, EventArgs e)
        {
            if (_server != null)
            {
                _server.DisconnectAll();
            }
        }

        private static void PrintLog()
        {
            Console.WriteLine($"(Log) Port: {_server.LocalPort}");
            Console.WriteLine($"(Log) Current number of users: {_clients.Count}");
            Console.WriteLine($"(Log) Is server running: {_server.IsRunning}");
        }

        private static void OnLogTimerElapsed(object sender, ElapsedEventArgs e)
        {
            PrintLog();
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
