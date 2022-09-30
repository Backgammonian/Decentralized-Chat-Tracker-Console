using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Layers;
using Networking.Messages;

namespace Networking
{
    public sealed class Server
    {
        private readonly EventBasedNetListener _listener;
        private readonly XorEncryptLayer _xor;
        private readonly NetManager _server;
        private readonly EncryptedPeers _clients;
        private readonly CancellationTokenSource _tokenSource;

        public Server()
        {
            _listener = new EventBasedNetListener();
            _xor = new XorEncryptLayer("VerySecretSymmetricXorPassword");
            _server = new NetManager(_listener, _xor);
            _server.ChannelsCount = NetworkingConstants.ChannelsCount;
            _server.DisconnectTimeout = NetworkingConstants.DisconnectionTimeoutMilliseconds;
            _clients = new EncryptedPeers();
            _clients.PeerAdded += OnClientAdded;
            _clients.PeerRemoved += OnClientRemoved;
            _tokenSource = new CancellationTokenSource();
        }

        public event EventHandler<NetEventArgs> MessageReceived;
        public event EventHandler<EncryptedPeerEventArgs> ClientAdded;
        public event EventHandler<EncryptedPeerEventArgs> ClientRemoved;

        public int LocalPort => _server.LocalPort;
        public byte ChannelsCount => _server.ChannelsCount;
        public IEnumerable<EncryptedPeer> Clients => _clients.List;

        private void OnClientAdded(object sender, EncryptedPeerEventArgs e)
        {
            ClientAdded?.Invoke(this, e);
        }

        private void OnClientRemoved(object sender, EncryptedPeerEventArgs e)
        {
            ClientRemoved?.Invoke(this, e);
        }

        private async Task Run(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                _server.PollEvents();
                await Task.Delay(15);
            }
        }

        public EncryptedPeer GetClientByPeerID(int peerID)
        {
            return _clients.Get(peerID);
        }

        public void Stop()
        {
            _tokenSource.Cancel();
            _server.Stop();
        }

        public void DisconnectAll()
        {
            _server.DisconnectAll();
        }

        public void SendToAll(BaseMessage message)
        {
            foreach (var client in _clients.EstablishedList)
            {
                client.SendEncrypted(message, 0);
            }
        }

        public async Task StartListening(int port)
        {
            _server.Start(port);

            _listener.ConnectionRequestEvent += request => request.AcceptIfKey("ToChatTracker");

            _listener.PeerConnectedEvent += peer =>
            {
                var client = new EncryptedPeer(peer);
                _clients.Add(client);
            };

            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                _clients.Remove(peer.Id);

                System.Diagnostics.Debug.WriteLine("(Server) Client {0} disconnected", peer.EndPoint);
            };

            _listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod) =>
            {
                var client = _clients.Get(fromPeer.Id);

                if (client != null &&
                    client.IsSecurityEnabled &&
                    client.TryDecryptReceivedData(dataReader, out NetworkMessageType type, out string json))
                {
                    MessageReceived?.Invoke(this, new NetEventArgs(client, type, json));
                }
                else
                if (client != null &&
                    !client.IsSecurityEnabled &&
                    dataReader.TryGetBytesWithLength(out byte[] publicKey) &&
                    dataReader.TryGetBytesWithLength(out byte[] signaturePublicKey))
                {
                    client.ApplyKeys(publicKey, signaturePublicKey);
                    client.SendPublicKeys();
                }

                dataReader.Recycle();
            };

            var token = _tokenSource.Token;
            await Run(token);
        }
    }
}
