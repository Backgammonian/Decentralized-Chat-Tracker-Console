using System;
using System.Timers;
using System.Collections.Generic;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Networking.Utils;
using Networking.Messages;

namespace Networking
{
    public sealed class EncryptedPeer
    {
        private readonly NetPeer _peer;
        private readonly CryptographyModule _cryptography;
        private ulong _incomingSegmentNumber;
        private ulong _outcomingSegmentNumber;

        private readonly Timer _durationTimer;
        private TimeSpan _connectionDuration;
        private readonly Timer _disconnectTimer;

        private const int _speedTimerInterval = 100;

        private long _oldAmountOfDownloadedBytes, _newAmountOfDownloadedBytes;
        private DateTime _oldDownloadTimeStamp, _newDownloadTimeStamp;
        private long _bytesDownloaded;
        private readonly Timer _downloadSpeedCounter;
        private double _downloadSpeed;
        private readonly Queue<double> _downloadSpeedValues;

        private long _oldAmountOfUploadedBytes, _newAmountOfUploadedBytes;
        private DateTime _oldUploadTimeStamp, _newUploadTimeStamp;
        private long _bytesUploaded;
        private readonly Timer _uploadSpeedCounter;
        private double _uploadSpeed;
        private readonly Queue<double> _uploadSpeedValues;

        public EncryptedPeer(NetPeer peer)
        {
            _peer = peer;
            _cryptography = new CryptographyModule();
            _incomingSegmentNumber = RandomGenerator.GetRandomULong();
            _outcomingSegmentNumber = RandomGenerator.GetRandomULong();

            StartTime = DateTime.Now;

            _durationTimer = new Timer();
            _durationTimer.Interval = 1000;
            _durationTimer.Elapsed += OnDurationTimerTick;
            _durationTimer.Start();

            _disconnectTimer = new Timer();
            _disconnectTimer.Interval = NetworkingConstants.DisconnectionTimeoutMilliseconds;
            _disconnectTimer.Elapsed += OnDisconnectTimerTick;
            _disconnectTimer.Start();

            _downloadSpeedValues = new Queue<double>();
            _downloadSpeedCounter = new Timer();
            _downloadSpeedCounter.Interval = _speedTimerInterval;
            _downloadSpeedCounter.Elapsed += OnDownloadSpeedCounterTick;
            _downloadSpeedCounter.Start();

            _uploadSpeedValues = new Queue<double>();
            _uploadSpeedCounter = new Timer();
            _uploadSpeedCounter.Interval = _speedTimerInterval;
            _uploadSpeedCounter.Elapsed += OnUploadSpeedCounterTick;
            _uploadSpeedCounter.Start();
        }

        public event EventHandler<EncryptedPeerEventArgs> PeerDisconnected;

        public bool IsSecurityEnabled => _cryptography.IsEnabled;
        public int Id => _peer.Id;
        public IPEndPoint EndPoint => _peer.EndPoint;
        public int Ping => _peer.Ping;
        public long PacketLossPercent => _peer.Statistics.PacketLossPercent;
        public ConnectionState ConnectionState => _peer.ConnectionState;
        public TimeSpan ConnectionDuration => _connectionDuration;
        public double DownloadSpeed => _downloadSpeed;
        public double UploadSpeed => _uploadSpeed;
        public DateTime StartTime { get; }

        private void OnDurationTimerTick(object sender, ElapsedEventArgs e)
        {
            _connectionDuration = DateTime.Now - StartTime;
        }

        private void OnDisconnectTimerTick(object sender, ElapsedEventArgs e)
        {
            _disconnectTimer.Stop();

            if (!IsSecurityEnabled)
            {
                Disconnect();
            }
        }

        private void OnDownloadSpeedCounterTick(object sender, ElapsedEventArgs e)
        {
            _oldAmountOfDownloadedBytes = _newAmountOfDownloadedBytes;
            _newAmountOfDownloadedBytes = _bytesDownloaded;

            _oldDownloadTimeStamp = _newDownloadTimeStamp;
            _newDownloadTimeStamp = DateTime.Now;

            var value = (_newAmountOfDownloadedBytes - _oldAmountOfDownloadedBytes) / (_newDownloadTimeStamp - _oldDownloadTimeStamp).TotalSeconds;
            _downloadSpeedValues.Enqueue(value);

            if (_downloadSpeedValues.Count > 20)
            {
                _downloadSpeedValues.Dequeue();
            }

            _downloadSpeed = _downloadSpeedValues.CalculateAverageValue();
        }

        private void OnUploadSpeedCounterTick(object sender, ElapsedEventArgs e)
        {
            _oldAmountOfUploadedBytes = _newAmountOfUploadedBytes;
            _newAmountOfUploadedBytes = _bytesUploaded;

            _oldUploadTimeStamp = _newUploadTimeStamp;
            _newUploadTimeStamp = DateTime.Now;

            var value = (_newAmountOfUploadedBytes - _oldAmountOfUploadedBytes) / (_newUploadTimeStamp - _oldUploadTimeStamp).TotalSeconds;
            _uploadSpeedValues.Enqueue(value);

            if (_uploadSpeedValues.Count > 20)
            {
                _uploadSpeedValues.Dequeue();
            }

            _uploadSpeed = _uploadSpeedValues.CalculateAverageValue();
        }

        public void SendPublicKeys()
        {
            var data = new NetDataWriter();

            var publicKey = _cryptography.PublicKey;
            data.Put(publicKey.Length);
            data.Put(publicKey);

            var signaturePublicKey = _cryptography.SignaturePublicKey;
            data.Put(signaturePublicKey.Length);
            data.Put(signaturePublicKey);

            data.Put(_incomingSegmentNumber);

            _peer.Send(data, 0, DeliveryMethod.ReliableOrdered);
        }

        public void ApplyKeys(byte[] publicKey, byte[] signaturePublicKey, ulong recepientsIncomingSegmentNumber)
        {
            if (_cryptography.TrySetKeys(publicKey, signaturePublicKey))
            {
                _outcomingSegmentNumber = recepientsIncomingSegmentNumber;
            }
            else
            {
                Disconnect();
            }
        }

        public void SendEncrypted(BaseMessage message)
        {
            SendEncrypted(message, RandomGenerator.GetPseudoRandomByte(0, NetworkingConstants.ChannelsCount));
        }

        public void SendEncrypted(BaseMessage message, byte channelNumber)
        {
            var messageContent = message.GetContent();
            messageContent.Put(_outcomingSegmentNumber);

            SendEncrypted(messageContent.Data, channelNumber);
        }

        private void SendEncrypted(byte[] message, byte channelNumber)
        {
            if (!IsSecurityEnabled)
            {
                return;
            }

            if (_cryptography.TryCreateSignature(message, out byte[] signature) &&
                message.TryCompressByteArray(out byte[] compressedMessage) &&
                _cryptography.TryEncrypt(compressedMessage, out byte[] encryptedMessage, out byte[] iv))
            {
                var outcomingMessage = new NetDataWriter();
                outcomingMessage.PutBytesWithLength(encryptedMessage);
                outcomingMessage.PutBytesWithLength(signature);
                outcomingMessage.PutBytesWithLength(iv);
                outcomingMessage.Put(CRC32.Compute(encryptedMessage));

                _peer.Send(outcomingMessage.Data, channelNumber, DeliveryMethod.ReliableOrdered);

                _bytesUploaded += outcomingMessage.Data.Length;

                _outcomingSegmentNumber += 1;
                _outcomingSegmentNumber = _outcomingSegmentNumber == ulong.MaxValue ? 0 : _outcomingSegmentNumber;
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
                incomingDataReader.TryGetUInt(out uint receivedCrc32) &&
                CRC32.Compute(encryptedMessage) == receivedCrc32 &&
                _cryptography.TryDecrypt(encryptedMessage, iv, out byte[] decryptedMessage) &&
                decryptedMessage.TryDecompressByteArray(out byte[] decompressedMessage) &&
                _cryptography.TryVerifySignature(decompressedMessage, signature))
            {
                var messageReader = new NetDataReader(decompressedMessage);

                if (messageReader.TryGetByte(out byte type) &&
                    Enum.TryParse(typeof(NetworkMessageType), type + "", out object networkMessageType) &&
                    networkMessageType != null &&
                    messageReader.TryGetString(out string json) &&
                    messageReader.TryGetULong(out ulong recepientsOutcomingSegmentNumber) &&
                    recepientsOutcomingSegmentNumber == _incomingSegmentNumber)
                {
                    _incomingSegmentNumber += 1;
                    _incomingSegmentNumber = _incomingSegmentNumber == ulong.MaxValue ? 0 : _incomingSegmentNumber;

                    _bytesDownloaded += incomingDataReader.RawDataSize;

                    messageType = (NetworkMessageType)networkMessageType;
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

            _durationTimer.Stop();
            _disconnectTimer.Stop();
            _downloadSpeedCounter.Stop();
            _uploadSpeedCounter.Stop();

            PeerDisconnected?.Invoke(this, new EncryptedPeerEventArgs(this));
        }
    }
}
