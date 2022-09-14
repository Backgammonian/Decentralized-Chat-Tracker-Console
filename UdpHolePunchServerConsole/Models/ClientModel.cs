﻿using System;
using Networking;
using Networking.Messages;

namespace UdpHolePunchServerConsole.Models
{
    public sealed class ClientModel
    {
        private readonly EncryptedPeer _peer;
        private long _messagesFromClientNumber;
        private string _nickname = string.Empty;

        public ClientModel(EncryptedPeer peer, string id, string nickname)
        {
            _peer = peer;
            _nickname = nickname;
            ID = id;
            ConnectionTime = DateTime.Now;
        }

        public int PeerID => _peer.Id;
        public string EndPoint => _peer.EndPoint.ToString();
        public string Nickname => _nickname;
        public long MessagesFromClientNumber => _messagesFromClientNumber;
        public string ID { get; }
        public DateTime ConnectionTime { get; }

        public void IncreaseMessageCount()
        {
            _messagesFromClientNumber += 1;
        }

        public void UpdateNickname(string newNickname)
        {
            _nickname = newNickname;
        }

        private void Send(BaseMessage message)
        {
            _peer.SendEncrypted(message);
        }

        public void SendConnectionResponseMessage(string desiredClientID, string desiredClientEndPoint)
        {
            var userConnectionResponseMessage = new UserConnectionResponseMessage(desiredClientID, desiredClientEndPoint);
            Send(userConnectionResponseMessage);
        }

        public void SendForwardedConnectionRequestMessage(string id, string endPoint)
        {
            var forwardedConnectionRequestMessage = new ForwardedConnectionRequestMessage(id, endPoint);
            Send(forwardedConnectionRequestMessage);
        }

        public void SendCommandReceiptMessage(string commandID)
        {
            var commandReceiptNotificationMessage = new CommandReceiptNotificationMessage(commandID);
            Send(commandReceiptNotificationMessage);
        }

        public void SendCommandErrorMessage(string wrongCommand, string argument)
        {
            var commandErrorMessage = new CommandToTrackerErrorMessage(wrongCommand, argument);
            Send(commandErrorMessage);
        }

        public void SendPingResponseCommand()
        {
            var pingResponse = new PingResponseMessage(_peer.Ping);
            Send(pingResponse);
        }

        public void SendTimeResponseCommand()
        {
            var timeResponse = new TimeResponseMessage();
            Send(timeResponse);
        }

        public void SendUserNotFoundErrorMessage(string userInfo)
        {
            var userNotFoundMessage = new UserNotFoundErrorMessage(userInfo);
            Send(userNotFoundMessage);
        }

        public void SendIntroductionResponseMessage()
        {
            var responseMessage = new IntroduceClientToTrackerResponseMessage(EndPoint);
            Send(responseMessage);
        }

        public void SendListOfUsersWithSpecifiedNickname(UserInfoFromTracker[] users)
        {
            var message = new ListOfUsersWithDesiredNicknameMessage(users);
            Send(message);
        }
    }
}