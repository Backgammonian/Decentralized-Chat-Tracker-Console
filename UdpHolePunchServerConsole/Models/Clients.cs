using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Networking;
using Networking.Messages;

namespace UdpHolePunchServerConsole.Models
{
    public sealed class Clients
    {
        //end point in string format, ClientModel object
        private readonly ConcurrentDictionary<string, ClientModel> _clients;

        public Clients()
        {
            _clients = new ConcurrentDictionary<string, ClientModel>();
        }

        public event EventHandler<EventArgs> ClientAdded;
        public event EventHandler<EventArgs> ClientRemoved;

        public IEnumerable<ClientModel> List => _clients.Values;
        public int Count => _clients.Count;

        public bool Has(string endPoint)
        {
            return _clients.ContainsKey(endPoint);
        }

        public ClientModel Get(EncryptedPeer client)
        {
            var endPoint = client.EndPoint.ToString();
            if (_clients.TryGetValue(endPoint, out var desiredClient))
            {
                return desiredClient;
            }

            return null;
        }

        public ClientModel GetByUserID(string id)
        {
            try
            {
                return _clients.Values.First(client => client.ID == id);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);

                return null;
            }
        }

        public UserInfoFromTracker[] GetByNickname(string nickname)
        {
            return _clients.Values
                .Where(client => client.Nickname == nickname)
                .Select(client => new UserInfoFromTracker(client.Nickname, client.ID))
                .ToArray();
        }

        public void Add(ClientModel client)
        {
            if (!Has(client.EndPoint) &&
                _clients.TryAdd(client.EndPoint, client))
            {
                ClientAdded?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Remove(ClientModel client)
        {
            var endPoint = client.EndPoint;
            if (Has(endPoint) &&
                _clients.TryRemove(endPoint, out ClientModel _))
            {
                ClientRemoved?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
