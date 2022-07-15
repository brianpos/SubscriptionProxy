// <copyright file="WebsocketManager.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using argonaut_subscription_server_proxy.Models;
using fhirCsModels4B = Hl7.Fhir.Model;

namespace argonaut_subscription_server_proxy.Managers
{
    /// <summary>Manager for websockets.</summary>
    public class WebsocketManager
    {
        /// <summary>The keepalive timeout in ticks.</summary>
        private const long _keepaliveTimeoutTicks = 29 * TimeSpan.TicksPerSecond;         // 29 seconds

        /// <summary>The instance for singleton pattern.</summary>
        private static WebsocketManager _instance;

        /// <summary>Dictionary of unique identifier informations.</summary>
        private Dictionary<Guid, WebsocketClientInformation> _guidInfoDict;

        /// <summary>Dictionary of subscription infos.</summary>
        private Dictionary<string, List<WebsocketClientInformation>> _subscriptionInfosDict;

        /// <summary>Dictionary of websocket subscription binding tokens.</summary>
        private Dictionary<Guid, SubscriptionWsBindingToken> _guidTokenDict;

        /// <summary>The FHIR R4 clients and timeouts.</summary>
        private ConcurrentDictionary<Guid, long> _clientsAndTimeouts;

        /// <summary>
        /// Prevents a default instance of the
        /// <see cref="WebsocketManager"/> class from being created.
        /// </summary>
        private WebsocketManager()
        {
            _guidInfoDict = new ();
            _subscriptionInfosDict = new ();
            _guidTokenDict = new ();
            _clientsAndTimeouts = new ();
        }

        /// <summary>Initializes this object.</summary>
        public static void Init()
        {
            // make an instance
            CheckOrCreateInstance();
        }

        /// <summary>Registers the client described by client.</summary>
        /// <param name="client">The client.</param>
        public static void RegisterClient(WebsocketClientInformation client)
        {
            if (client == null)
            {
                return;
            }

            // add this client to our dictionary
            if (!_instance._guidInfoDict.ContainsKey(client.Uid))
            {
                _instance._guidInfoDict.Add(client.Uid, client);
            }

            if (!_instance._clientsAndTimeouts.ContainsKey(client.Uid))
            {
                _instance._clientsAndTimeouts.TryAdd(client.Uid, DateTime.Now.Ticks + _keepaliveTimeoutTicks);
            }
        }

        /// <summary>Unregisters the client described by GUID.</summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        public static void UnregisterClient(Guid clientGuid)
        {
            if (clientGuid == Guid.Empty)
            {
                return;
            }

            if (_instance._guidInfoDict.ContainsKey(clientGuid))
            {
                _instance._guidInfoDict.Remove(clientGuid);
            }

            if (_instance._clientsAndTimeouts.ContainsKey(clientGuid))
            {
                _instance._clientsAndTimeouts.TryRemove(clientGuid, out long _);
            }
        }

        /// <summary>Process the keepalives.</summary>
        /// <param name="currentTicks">The current ticks.</param>
        /// <param name="timeString">  The time string.</param>
        public static void ProcessKeepalives(long currentTicks, string timeString)
        {
            List<Guid> clientsToRemove = new List<Guid>();

            // traverse the dictionary looking for clients we need to send messages to
            foreach (KeyValuePair<Guid, long> kvp in _instance._clientsAndTimeouts)
            {
                // check timeout
                if (currentTicks > kvp.Value)
                {
                    // enqueue a message for this client
                    if (WebsocketManager.TryGetClient(kvp.Key, out WebsocketClientInformation client))
                    {
                        // enqueue a keepalive message
                        client.MessageQ.Enqueue($"keepalive {timeString}");
                    }
                    else
                    {
                        // client is gone, stop sending (cannot remove inside iterator)
                        clientsToRemove.Add(kvp.Key);
                    }
                }
            }

            if (clientsToRemove.Count > 0)
            {
                foreach (Guid clientGuid in clientsToRemove)
                {
                    _instance._clientsAndTimeouts.TryRemove(clientGuid, out _);
                }
            }
        }

        /// <summary>Updates the timeout for sent message described by clientGuid.</summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        public static void UpdateTimeoutForSentMessage(Guid clientGuid)
        {
            // update our keepalive timeout
            _instance._clientsAndTimeouts[clientGuid] = DateTime.Now.Ticks + _keepaliveTimeoutTicks;
        }

        /// <summary>Attempts to get client a WebsocketClientInformation from the given GUID.</summary>
        /// <param name="clientGuid">  Unique identifier.</param>
        /// <param name="client">[out] The client.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetClient(Guid clientGuid, out WebsocketClientInformation client)
        {
            if (clientGuid == Guid.Empty)
            {
                client = null;
                return false;
            }

            // check for this client existing
            if (_instance._guidInfoDict.ContainsKey(clientGuid))
            {
                // set our client object
                client = _instance._guidInfoDict[clientGuid];

                // success
                return true;
            }

            // not found
            client = null;

            // failure
            return false;
        }

        /// <summary>Registers the token described by token.</summary>
        /// <exception cref="ArgumentNullException">Thrown when one or more required arguments are null.</exception>
        /// <param name="token">The token.</param>
        public static void RegisterToken(SubscriptionWsBindingToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            _instance._guidTokenDict.Add(token.Token, token);
        }

        /// <summary>Bind client with token.</summary>
        /// <param name="tokenGuid"> Unique identifier for the token.</param>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool BindClientWithToken(Guid tokenGuid, Guid clientGuid)
        {
            if (!_instance._guidTokenDict.ContainsKey(tokenGuid))
            {
                return false;
            }

            if (!_instance._guidInfoDict.ContainsKey(clientGuid))
            {
                return false;
            }

            if (!_instance._guidTokenDict[tokenGuid].BindToClient(clientGuid))
            {
                return false;
            }

            _instance._guidInfoDict[clientGuid].BoundTokenGuids.Add(tokenGuid);

            foreach (string subscriptionId in _instance._guidTokenDict[tokenGuid].SubscriptionIds)
            {
                if (!_instance._guidInfoDict[clientGuid].SubscriptionIdSet.Contains(subscriptionId))
                {
                    _instance._guidInfoDict[clientGuid].SubscriptionIdSet.Add(subscriptionId);
                }

                if (!_instance._subscriptionInfosDict.ContainsKey(subscriptionId))
                {
                    _instance._subscriptionInfosDict.Add(subscriptionId, new List<WebsocketClientInformation>());
                    _instance._subscriptionInfosDict[subscriptionId].Add(_instance._guidInfoDict[clientGuid]);
                }
            }

            return true;
        }

        /// <summary>Expires and Removes the token described by tokenGuid.</summary>
        /// <param name="tokenGuid">Unique identifier for the token.</param>
        public static void ExpireToken(Guid tokenGuid)
        {
            if (!_instance._guidTokenDict.ContainsKey(tokenGuid))
            {
                return;
            }

            Guid clientGuid = _instance._guidTokenDict[tokenGuid].BoundClient;

            if (clientGuid == Guid.Empty)
            {
                _instance._guidTokenDict.Remove(tokenGuid);
                return;
            }

            if (!_instance._guidInfoDict.ContainsKey(clientGuid))
            {
                _instance._guidTokenDict.Remove(tokenGuid);
                return;
            }

            SubscriptionWsBindingToken token = _instance._guidTokenDict[tokenGuid];
            WebsocketClientInformation clientInfo = _instance._guidInfoDict[clientGuid];

            // if the client only has one token (this one), deactivate everything
            if (clientInfo.BoundTokenGuids.Count == 1)
            {
                foreach (string subscriptionId in token.SubscriptionIds)
                {
                    RemoveSubscriptionFromClient(subscriptionId, clientGuid);
                }
            }

            // TODO: for now, just assume it has been replaced with a newer replica
            _instance._guidTokenDict.Remove(tokenGuid);
            _instance._guidInfoDict[clientGuid].BoundTokenGuids.Remove(tokenGuid);

            return;
        }

        /// <summary>Removes the expired tokens.</summary>
        public static void RemoveExpiredTokens()
        {
            DateTime now = DateTime.Now;

            List<SubscriptionWsBindingToken> tokens = _instance._guidTokenDict.Values.ToList();
            foreach (SubscriptionWsBindingToken token in tokens)
            {
                if (token.ExpiresAt > now)
                {
                    ExpireToken(token.Token);
                }
            }
        }

        /// <summary>Adds a subscription to client to 'clientGuid'.</summary>
        /// <param name="subscriptionId">Identifier for the subscription.</param>
        /// <param name="clientGuid">    Unique identifier for the client.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool AddSubscriptionToClient(string subscriptionId, Guid clientGuid)
        {
            if (string.IsNullOrEmpty(subscriptionId))
            {
                return false;
            }

            if (!SubscriptionManagerR4.Exists(subscriptionId))
            {
                return false;
            }

            if (!_instance._guidInfoDict.ContainsKey(clientGuid))
            {
                return false;
            }

            _instance._guidInfoDict[clientGuid].SubscriptionIdSet.Add(subscriptionId);

            if (!_instance._subscriptionInfosDict.ContainsKey(subscriptionId))
            {
                _instance._subscriptionInfosDict.Add(subscriptionId, new List<WebsocketClientInformation>());
            }

            _instance._subscriptionInfosDict[subscriptionId].Add(_instance._guidInfoDict[clientGuid]);
            return true;
        }

        /// <summary>Removes the subscription from client.</summary>
        /// <param name="subscriptionId">Identifier for the subscription.</param>
        /// <param name="clientGuid">    Unique identifier for the client.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool RemoveSubscriptionFromClient(string subscriptionId, Guid clientGuid)
        {
            if (string.IsNullOrEmpty(subscriptionId) || (clientGuid == Guid.Empty))
            {
                return false;
            }

            if (_instance._guidInfoDict[clientGuid].SubscriptionIdSet.Contains(subscriptionId))
            {
                _instance._guidInfoDict[clientGuid].SubscriptionIdSet.Remove(subscriptionId);
            }

            if (_instance._subscriptionInfosDict.ContainsKey(subscriptionId) &&
                _instance._subscriptionInfosDict[subscriptionId].Contains(_instance._guidInfoDict[clientGuid]))
            {
                _instance._subscriptionInfosDict[subscriptionId].Remove(_instance._guidInfoDict[clientGuid]);

                if (_instance._subscriptionInfosDict[subscriptionId].Count == 0)
                {
                    _instance._subscriptionInfosDict.Remove(subscriptionId);
                }
            }

            return true;
        }

        /// <summary>Queue messages for subscription.</summary>
        /// <param name="subscription">The subscription.</param>
        /// <param name="json">        The resource.</param>
        public static void QueueMessagesForSubscription(
            fhirCsModels4B.Subscription subscription,
            string json)
        {
            if (subscription == null)
            {
                return;
            }

            if (!_instance._subscriptionInfosDict.ContainsKey(subscription.Id))
            {
                return;
            }

            foreach (WebsocketClientInformation client in _instance._subscriptionInfosDict[subscription.Id])
            {
                // add this message to this client's queue (caller should have set it up correctly)
                client.MessageQ.Enqueue(json);
            }
        }

        /// <summary>Check or create instance.</summary>
        private static void CheckOrCreateInstance()
        {
            if (_instance == null)
            {
                _instance = new WebsocketManager();
            }
        }
    }
}
