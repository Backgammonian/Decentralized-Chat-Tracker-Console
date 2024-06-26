﻿using System;
using System.Timers;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using NetworkingLib.Messages;
using NetworkingLib.Utils;
using NetworkingLib.Cryptography;

namespace NetworkingLib
{
    public sealed class EncryptedPeer : IDisposable
    {
        private readonly NetPeer _peer;
        private readonly CryptographyModule _cryptography;
        private readonly SpeedCounter _downloadSpeedCounter;
        private readonly SpeedCounter _uploadSpeedCounter;
        private readonly Timer _disconnectTimer;
        private readonly Timer _pingUpdateTimer;
        private readonly Timer _durationTimer;
        private bool _isDisposed;

        public EncryptedPeer(NetPeer peer)
        {
            _peer = peer;
            _cryptography = new CryptographyModule();

            _durationTimer = new Timer();
            _durationTimer.Interval = 1000;
            _durationTimer.Elapsed += OnDurationTimerTick;
            _durationTimer.Start();

            _disconnectTimer = new Timer();
            _disconnectTimer.Interval = NetworkingConstants.DisconnectionTimeoutMilliseconds;
            _disconnectTimer.Elapsed += OnDisconnectTimerTick;
            _disconnectTimer.Start();

            _pingUpdateTimer = new Timer();
            _pingUpdateTimer.Interval = 200;
            _pingUpdateTimer.Elapsed += OnPingUdpateTimerTick;
            _pingUpdateTimer.Start();

            _downloadSpeedCounter = new SpeedCounter();
            _downloadSpeedCounter.Updated += OnDownloadSpeedCounterUpdated;
            _uploadSpeedCounter = new SpeedCounter();
            _uploadSpeedCounter.Updated += OnUploadSpeedCounterUpdated;

            StartTime = DateTime.Now;
        }

        public event EventHandler<EncryptedPeerEventArgs> PeerDisconnected;

        public DateTime StartTime { get; }
        public int Id => _peer.Id;
        public IPEndPoint EndPoint => new IPEndPoint(_peer.Address, _peer.Port);
        public ConnectionState ConnectionState => _peer.ConnectionState;
        public bool IsSecurityEnabled { get; private set; }
        public int Ping { get; private set; }
        public double DownloadSpeed { get; private set; }
        public double UploadSpeed { get; private set; }
        public long BytesDownloaded { get; private set; }
        public long BytesUploaded { get; private set; }
        public TimeSpan ConnectionDuration { get; private set; }

        private void OnDurationTimerTick(object sender, ElapsedEventArgs e)
        {
            ConnectionDuration = DateTime.Now - StartTime;
        }

        private void OnDisconnectTimerTick(object sender, ElapsedEventArgs e)
        {
            _disconnectTimer.Stop();

            if (!IsSecurityEnabled)
            {
                Disconnect();
            }
        }

        private void OnDownloadSpeedCounterUpdated(object sender, EventArgs e)
        {
            DownloadSpeed = _downloadSpeedCounter.Speed;
            BytesDownloaded = _downloadSpeedCounter.Bytes;
        }

        private void OnUploadSpeedCounterUpdated(object sender, EventArgs e)
        {
            UploadSpeed = _uploadSpeedCounter.Speed;
            BytesUploaded = _uploadSpeedCounter.Bytes;
        }

        private void OnPingUdpateTimerTick(object sender, ElapsedEventArgs e)
        {
            Ping = _peer.Ping;
        }

        public void SendPublicKeys()
        {
            var data = new NetDataWriter();
            var publicKey = _cryptography.PublicKey;
            //data.Put(publicKey.Length);
            //data.Put(publicKey);
            data.PutBytesWithLength(publicKey);
            var signaturePublicKey = _cryptography.SignaturePublicKey;
            //data.Put(signaturePublicKey.Length);
            //data.Put(signaturePublicKey);
            data.PutBytesWithLength(signaturePublicKey);

            _peer.Send(data, 0, DeliveryMethod.ReliableOrdered);
        }

        public bool ApplyKeys(byte[] publicKey, byte[] signaturePublicKey)
        {
            if (_cryptography.TrySetKeys(publicKey, signaturePublicKey))
            {
                IsSecurityEnabled = _cryptography.IsEnabled;

                return true;
            }
            else
            {
                Disconnect();

                return false;
            }
        }

        public void SendEncrypted(BaseMessage message, byte channelNumber)
        {
            if (!IsSecurityEnabled)
            {
                return;
            }

            if (channelNumber < 0 ||
                channelNumber >= _peer.NetManager.ChannelsCount)
            {
                return;
            }

            var content = message.GetContent(_cryptography.MyPublicKeyHash).Data;
            SendEncrypted(content, channelNumber);
        }

        private void SendEncrypted(byte[] message, byte channelNumber)
        {
            if (_cryptography.TrySignData(message, out byte[] signature) &&
                message.TryCompressByteArray(out byte[] compressedMessage) &&
                _cryptography.TryEncrypt(compressedMessage, out byte[] encryptedMessage, out byte[] iv))
            {
                var outcomingMessage = new NetDataWriter();
                outcomingMessage.PutBytesWithLength(encryptedMessage);
                outcomingMessage.PutBytesWithLength(signature);
                outcomingMessage.PutBytesWithLength(iv);

                _peer.Send(outcomingMessage, channelNumber, DeliveryMethod.ReliableOrdered);
                _uploadSpeedCounter.AddBytes(outcomingMessage.Data.Length);
            }
        }

        public bool TryDecryptReceivedData(NetPacketReader incomingDataReader, out NetworkMessageType messageType, out string outputJson)
        {
            messageType = NetworkMessageType.Empty;
            outputJson = string.Empty;

            if (!IsSecurityEnabled)
            {
                return false;
            }

            if (incomingDataReader.TryGetBytesWithLength(out byte[] encryptedMessage) &&
                incomingDataReader.TryGetBytesWithLength(out byte[] signature) &&
                incomingDataReader.TryGetBytesWithLength(out byte[] iv) &&
                _cryptography.TryDecrypt(encryptedMessage, iv, out byte[] decryptedMessage) &&
                decryptedMessage.TryDecompressByteArray(out byte[] decompressedMessage) &&
                _cryptography.TryVerifySignature(decompressedMessage, signature))
            {
                var messageReader = new NetDataReader(decompressedMessage);
                if (messageReader.TryGetString(out var recepientPublicKeyHash) &&
                    _cryptography.RecepientPublicKeyHash != string.Empty &&
                    recepientPublicKeyHash == _cryptography.RecepientPublicKeyHash &&
                    messageReader.TryGetByte(out byte typeByte) &&
                    typeByte.TryParseType(out NetworkMessageType type) &&
                    messageReader.TryGetString(out string json))
                {
                    _downloadSpeedCounter.AddBytes(incomingDataReader.RawDataSize);

                    messageType = type;
                    outputJson = json;

                    return true;
                }
            }

            return false;
        }

        public void Disconnect()
        {
            var id = _peer.Id;
            _peer.Disconnect();
            StopTimers();
            PeerDisconnected?.Invoke(this, new EncryptedPeerEventArgs(this));
            Dispose();
        }

        private void StopTimers()
        {
            _durationTimer.Stop();
            _disconnectTimer.Stop();
            _downloadSpeedCounter.Stop();
            _uploadSpeedCounter.Stop();
            _pingUpdateTimer.Stop();
        }

        public override string ToString()
        {
            return _peer.Address.ToString() + ":" + _peer.Port;
        }

        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    StopTimers();

                    _cryptography.Dispose();
                    _disconnectTimer.Dispose();
                    _durationTimer.Dispose();
                    _pingUpdateTimer.Dispose();
                    _downloadSpeedCounter.Dispose();
                    _uploadSpeedCounter.Dispose();
                }

                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
