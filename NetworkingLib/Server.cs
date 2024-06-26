﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Layers;
using NetworkingLib.Messages;
using Newtonsoft.Json.Linq;

namespace NetworkingLib
{
    public sealed class Server
    {
        private readonly EventBasedNetListener _listener;
        private readonly XorEncryptLayer _xor;
        private readonly NetManager _server;
        private readonly EncryptedPeers _clients;
        private readonly Task _listenTask;
        private readonly CancellationTokenSource _tokenSource;

        public Server()
        {
            _listener = new EventBasedNetListener();
            _xor = new XorEncryptLayer(NetworkingConstants.XorLayerPassword);
            _server = new NetManager(_listener, _xor);
            _server.ChannelsCount = NetworkingConstants.ChannelsCount;
            _server.DisconnectTimeout = NetworkingConstants.DisconnectionTimeoutMilliseconds;
            _clients = new EncryptedPeers();
            _clients.PeerAdded += OnClientAdded;
            _clients.PeerRemoved += OnClientRemoved;
            _tokenSource = new CancellationTokenSource();
            var token = _tokenSource.Token;
            _listenTask = new Task(async () => await Run(token));
        }

        public event EventHandler<NetEventArgs> MessageFromClientReceived;
        public event EventHandler<EncryptedPeerEventArgs> ClientAdded;
        public event EventHandler<EncryptedPeerEventArgs> ClientRemoved;

        public int LocalPort => _server.LocalPort;
        public byte ChannelsCount => _server.ChannelsCount;
        public bool IsRunning => _server.IsRunning;
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

        private void Configure(int port)
        {
            _server.Start(port);

            _listener.ConnectionRequestEvent += request => request.AcceptIfKey("ToServer");

            _listener.PeerConnectedEvent += peer =>
            {
                var client = new EncryptedPeer(peer);
                _clients.Add(client);

                Console.WriteLine($"(Server) Client {peer.Address}:{peer.Port} just connected");
            };

            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                _clients.Remove(peer.Id);

                Console.WriteLine($"(Server) Client {peer.Address}:{peer.Port} disconnected");
            };

            _listener.NetworkReceiveEvent += (fromPeer, dataReader, channel, deliveryMethod) =>
            {
                var client = _clients.Get(fromPeer.Id);

                Console.WriteLine($"(Server) Receive from {fromPeer.Address}:{fromPeer.Port}, Sec.: {client?.IsSecurityEnabled}");

                if (client == null)
                {
                    Console.WriteLine($"(Server) Something wrong with client {fromPeer.Address}:{fromPeer.Port}, Id {fromPeer.Id}");
                    return;
                }    

                if (client.IsSecurityEnabled &&
                    client.TryDecryptReceivedData(dataReader, out NetworkMessageType type, out string json))
                {
                    MessageFromClientReceived?.Invoke(this, new NetEventArgs(client, type, json));
                }
                else
                if (!client.IsSecurityEnabled && 
                    dataReader.TryGetBytesWithLength(out byte[] publicKey) &&
                    dataReader.TryGetBytesWithLength(out byte[] signaturePublicKey))
                {
                    var isKeyApplied = client.ApplyKeys(publicKey, signaturePublicKey);
                    client.SendPublicKeys();
                    
                    Console.WriteLine($"(Server) Is key from client {client.EndPoint} applied: {isKeyApplied}");
                }

                dataReader.Recycle();
            };
        }

        public void StartListening(int port)
        {
            Configure(port);
            _listenTask.Start();
        }

        public async Task StartListeningAsync(int port)
        {
            Configure(port);
            await _listenTask;
        }
    }
}
