// <copyright file="SubscriptionManagerR5.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
// using argonaut_subscription_server_proxy.Backport;
using argonaut_subscription_server_proxy.Models;
using argonaut_subscription_server_proxy.Zulip;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using fhirCsModels5 = Hl7.Fhir.Model;
using fhirCsValueSets5 = fhirCsR5.ValueSets;

namespace argonaut_subscription_server_proxy.Managers
{
    /// <summary>Manager for subscriptions.</summary>
    public class SubscriptionManagerR5
    {
        /// <summary>The maximum cached events.</summary>
        private const int MaxCachedEvents = 10;

        /// <summary>The instance for singleton pattern.</summary>
        private static SubscriptionManagerR5 _instance;

        /// <summary>Dictionary of subscriptions, by ID.</summary>
        private Dictionary<string, fhirCsModels5.Subscription> _idSubscriptionDict;

        /// <summary>The subscription event cache.</summary>
        private Dictionary<string, Dictionary<long, CachedNotificationEvent>> _subscriptionEventCache;

        /// <summary>Dictionary of identifier status.</summary>
        private Dictionary<string, long> _idEventCountDict;

        /// <summary>Dictionary of identifier last event ticks.</summary>
        private Dictionary<string, long> _idLastEventTicksDict;

        /// <summary>Dictionary of identifier locks.</summary>
        private Dictionary<string, object> _idLockDict;

        /// <summary>A random-number generator for this class.</summary>
        private Random _rand;

        private Dictionary<string, SubscriptionFilterNode> _resourceSubscriptionDict;
        private object _resourceSubscriptionDictLock;

        private HashSet<string> _supportedChannelTypes;

        private Queue<FhirClient> _clientQ;
        private object _clientQLock;

        private Thread _cleanUpThread;

        private Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver _contractResolver;

        private FhirJsonSerializer _fhirSerializer;
        private FhirJsonParser _fhirParser;

        /// <summary>
        /// Prevents a default instance of the
        /// <see cref="SubscriptionManagerR5"/> class from being created.
        /// </summary>
        private SubscriptionManagerR5()
        {
            // create our index objects
            _idSubscriptionDict = new ();
            _subscriptionEventCache = new ();
            _idEventCountDict = new ();
            _idLastEventTicksDict = new ();
            _idLockDict = new ();
            _resourceSubscriptionDict = new ();
            _resourceSubscriptionDictLock = new ();
            _rand = new ();
            _supportedChannelTypes = new ()
            {
                "rest-hook",
                "websocket",
                "email",
            };

            _clientQ = new ();
            _clientQLock = new ();

            // create our clean-up thread
            _cleanUpThread = new Thread(new ThreadStart(CleanUpThreadFunc));
            _cleanUpThread.IsBackground = true;
            _cleanUpThread.Start();

            // serialization related
            _contractResolver = new ();
            _fhirSerializer = new ();
            _fhirParser = new ();
        }

        /// <summary>Gets the current.</summary>
        public static SubscriptionManagerR5 Current => _instance;

        /// <summary>Initializes this object.</summary>
        public static void Init()
        {
            // make an instance
            CheckOrCreateInstance();
        }

        /// <summary>Gets a list of all currently known Subscriptions.</summary>
        /// <returns>The Subscription list.</returns>
        public static List<fhirCsModels5.Subscription> GetSubscriptionList()
        {
            // return our list of known subscriptions
            return _instance._idSubscriptionDict.Values.ToList<fhirCsModels5.Subscription>();
        }

        /// <summary>Gets subscriptions bundle.</summary>
        /// <returns>The subscriptions bundle.</returns>
        public static fhirCsModels5.Bundle GetSubscriptionsBundle()
        {
            return _instance._GetSubscriptionsBundle();
        }

        /// <summary>Handles a POST of a Subscription object.</summary>
        /// <param name="content">       The content.</param>
        /// <param name="subscription">  [out] The subscription.</param>
        /// <param name="statusCode">    [out] The status code.</param>
        /// <param name="failureContent">[out] The failure content.</param>
        public static void HandlePost(
            string content,
            out fhirCsModels5.Subscription subscription,
            out HttpStatusCode statusCode,
            out string failureContent)
        {
            _instance._HandlePost(
                content,
                out subscription,
                out statusCode,
                out failureContent);
        }

        /// <summary>Handles the delete described by request.</summary>
        /// <param name="request">The request.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool HandleDelete(HttpRequest request)
        {
            if (request == null)
            {
                return false;
            }

            return _instance._HandleDelete(request);
        }

        /// <summary>Process the encounter described by content.</summary>
        /// <param name="content"> The content.</param>
        /// <param name="location">The location.</param>
        public static void ProcessEncounter(string content, string location)
        {
            _ = System.Threading.Tasks.Task.Run((Action)(() => _instance._ProcessEncounter(content, location)));
        }

        /// <summary>URL for subscription.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <returns>A string.</returns>
        public static string UrlForSubscription(string subscriptionId)
        {
            return new Uri(
                new Uri(Program.Configuration["Server_Public_Url"], UriKind.Absolute),
                new Uri($"/r5/Subscription/{subscriptionId}", UriKind.Relative))
                .ToString();
        }

        /// <summary>Attempts to get subscription a Subscription from the given string.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="subscription">  [out] The subscription.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetSubscription(string subscriptionId, out fhirCsModels5.Subscription subscription)
        {
            if (_instance._idSubscriptionDict.ContainsKey(subscriptionId))
            {
                subscription = _instance._idSubscriptionDict[subscriptionId];
                return true;
            }

            subscription = null;
            return false;
        }

        /// <summary>Attempts to get serialized subscription a string from the given string.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="serialized">    [out] The serialized.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetSerialized(string subscriptionId, out string serialized)
        {
            if (TryGetSubscription(subscriptionId, out fhirCsModels5.Subscription subscription))
            {
                serialized = JsonSerializer.Serialize(subscription);
                return true;
            }

            serialized = null;
            return false;
        }

        /// <summary>Determine if 'subscriptionId' exists.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool Exists(string subscriptionId)
        {
            if (string.IsNullOrEmpty(subscriptionId))
            {
                return false;
            }

            if (subscriptionId.StartsWith("Subscription/", StringComparison.Ordinal))
            {
                return _instance._idSubscriptionDict.ContainsKey(
                    subscriptionId.Replace("Subscription/", string.Empty, StringComparison.Ordinal));
            }

            return _instance._idSubscriptionDict.ContainsKey(subscriptionId);
        }

        /// <summary>Gets subscriptions bundle.</summary>
        /// <param name="queryCollection">(Optional) Collection of queries.</param>
        /// <returns>The subscriptions bundle.</returns>
        private fhirCsModels5.Bundle _GetSubscriptionsBundle(IQueryCollection queryCollection = null)
        {
            fhirCsModels5.Bundle bundle = new ()
            {
                Type = Bundle.BundleType.Searchset,
                Total = _idSubscriptionDict.Count,
                Timestamp = DateTimeOffset.Now.ToUniversalTime(),
                Meta = new ()
                {
                    LastUpdated = DateTimeOffset.Now.ToUniversalTime(),
                },
                Entry = new (),
            };

            foreach (fhirCsModels5.Subscription subscription in _idSubscriptionDict.Values)
            {
                string subscriptionUrl = Program.UrlForR5ResourceId("Subscription", subscription.Id);

                if ((queryCollection == null) || (queryCollection.Count == 0))
                {
                    bundle.Entry.Add(new ()
                    {
                        FullUrl = subscriptionUrl,
                        Resource = (fhirCsModels5.Resource)subscription,
                        Search = new ()
                        {
                            Mode = Bundle.SearchEntryMode.Match,
                        },
                    });

                    continue;
                }

                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> query in queryCollection)
                {
                    switch (query.Key)
                    {
                        case "_id":
                            if (query.Value.Contains(subscription.Id, StringComparer.OrdinalIgnoreCase))
                            {
                                bundle.Entry.Add(new ()
                                {
                                    FullUrl = subscriptionUrl,
                                    Resource = subscription,
                                    Search = new ()
                                    {
                                        Mode = Bundle.SearchEntryMode.Match,
                                    },
                                });
                            }

                            break;

                        case "status":
                            if (query.Value.Contains(subscription.Status.ToString(), StringComparer.OrdinalIgnoreCase))
                            {
                                bundle.Entry.Add(new ()
                                {
                                    FullUrl = subscriptionUrl,
                                    Resource = subscription,
                                    Search = new ()
                                    {
                                        Mode = Bundle.SearchEntryMode.Match,
                                    },
                                });
                            }

                            break;

                        case "url":
                            if (query.Value.Contains(subscription.Endpoint, StringComparer.OrdinalIgnoreCase))
                            {
                                bundle.Entry.Add(new ()
                                {
                                    FullUrl = subscriptionUrl,
                                    Resource = subscription,
                                    Search = new ()
                                    {
                                        Mode = Bundle.SearchEntryMode.Match,
                                    },
                                });
                            }

                            break;

                        case "criteria":

                            if (query.Value.Contains(subscription.Topic, StringComparer.OrdinalIgnoreCase))
                            {
                                bundle.Entry.Add(new ()
                                {
                                    FullUrl = subscriptionUrl,
                                    Resource = subscription,
                                    Search = new ()
                                    {
                                        Mode = Bundle.SearchEntryMode.Match,
                                    },
                                });
                            }
                            else
                            {
                                foreach (fhirCsModels5.SubscriptionFilterBy filter in subscription.FilterBy)
                                {
                                    string serialized = string.IsNullOrEmpty(filter.ResourceType)
                                        ? string.Empty
                                        : filter.ResourceType + "?";

                                    if (string.IsNullOrEmpty(filter.SearchModifier) ||
                                        (filter.SearchModifier == fhirCsModels5.SubscriptionFilterBySearchModifierCodes.EQUALS) ||
                                        (filter.SearchModifier == fhirCsModels5.SubscriptionFilterBySearchModifierCodes.EQ))
                                    {
                                        serialized += $"{filter.SearchParamName}={filter.Value}";
                                    }
                                    else
                                    {
                                        serialized += $"{filter.SearchParamName}:{filter.SearchModifier}={filter.Value}";
                                    }

                                    string criteria = filter.SearchParamName;

                                    if (!string.IsNullOrEmpty(filter.SearchModifier))
                                    {
                                        criteria += ":" + filter.SearchModifier;
                                    }

                                    criteria += filter.Value;

                                    if (query.Value.Contains(criteria, StringComparer.OrdinalIgnoreCase))
                                    {
                                        bundle.Entry.Add(new ()
                                        {
                                            FullUrl = subscriptionUrl,
                                            Resource = subscription,
                                            Search = new ()
                                            {
                                                Mode = Bundle.SearchEntryMode.Match,
                                            },
                                        });

                                        break;
                                    }
                                }
                            }

                            break;
                    }
                }
            }

            bundle.Total = bundle.Entry.Count;

            // return our bundle
            return bundle;
        }

        /// <summary>Handles the delete described by request.</summary>
        /// <param name="request">The request.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool _HandleDelete(HttpRequest request)
        {
            // pull the URL components out of the request
            string[] components = request.Path.ToUriComponent().Split('/');

            bool foundSubscriptionComponent = false;

            // traverse components
            for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                // grab this component
                string component = components[componentIndex];

                // make sure that we hit a 'Subscription' component
                if (component.Equals("subscription", StringComparison.OrdinalIgnoreCase))
                {
                    foundSubscriptionComponent = true;
                    continue;
                }

                // only look for a GUID AFTER we have the subscription part
                if (foundSubscriptionComponent)
                {
                    // attempt to parse
                    if (Guid.TryParse(component, out _))
                    {
                        // remove this entry
                        return Remove(component);
                    }
                }
            }

            // still here is failure
            return false;
        }

        /// <summary>Removes the subscription specified by ID.</summary>
        /// <param name="id">The identifier.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id) || (!_idSubscriptionDict.ContainsKey(id)))
            {
                return false;
            }

            List<string> keysToRemove = new List<string>();

            // traverse trackings looking for this subscription
            foreach (KeyValuePair<string, SubscriptionFilterNode> kvp in _resourceSubscriptionDict)
            {
                lock (_resourceSubscriptionDictLock)
                {
                    kvp.Value.RemoveSubscription(id, out bool isEmpty);

                    if (isEmpty)
                    {
                        keysToRemove.Add(id);
                    }
                }
            }

            if (keysToRemove.Any())
            {
                lock (_resourceSubscriptionDictLock)
                {
                    foreach (string key in keysToRemove)
                    {
                        _resourceSubscriptionDict.Remove(key);
                    }
                }
            }

            // remove from the main dictionary
            _idSubscriptionDict.Remove(id);

            // remove from event cache
            _subscriptionEventCache.Remove(id);

            // remove from status
            _idEventCountDict.Remove(id);
            _idLastEventTicksDict.Remove(id);

            // remove from the lock dictionary
            if (_idLockDict.ContainsKey(id))
            {
                _idLockDict.Remove(id);
            }

            // log this addition
            Console.WriteLine($" <<< removed Subscription: {id}");

            // success
            return true;
        }

        /// <summary>Gets encounter patient groups.</summary>
        /// <param name="encounter">The encounter.</param>
        /// <param name="groups">   [out] The list.</param>
        private void GetEncounterPatientGroupKeys(Encounter encounter, out List<string> groups)
        {
            FhirClient client = null;
            groups = new List<string>();

            // attempt to get groups this patient may belong to
            try
            {
                // get a FHIR client
#pragma warning disable CA2000 // Dispose objects before losing scope
                if (!TryGetFhirClient(out client))
#pragma warning restore CA2000 // Dispose objects before losing scope
                {
                    return;
                }

                // ask for the groups for this patient
                Bundle results = client.Search<Group>(new string[] { $"member={encounter.Subject.Reference}" });

                if ((results != null) &&
                    (results.Entry != null) &&
                    (results.Entry.Count > 0))
                {
                    // traverse entries
                    foreach (Bundle.EntryComponent entry in results.Entry)
                    {
                        groups.Add($"patient:{entry.Resource.TypeName}/{entry.Resource.Id}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProcessEncounter <<< failed to get groups, ignoring: {ex.Message}");
            }
            finally
            {
                if (client != null)
                {
                    // return to our queue
                    ReturnFhirClientToQ(ref client);
                }
            }
        }

        /// <summary>Process the encounter described by content.</summary>
        /// <param name="content"> The content.</param>
        /// <param name="location">The location.</param>
        private void _ProcessEncounter(string content, string location)
        {
            List<fhirCsModels5.Subscription> subscriptions = new ();

            fhirCsModels5.Encounter csEncounter = null;
            Encounter sdkEncounter = null;

            // check to see if this resource is tracked (any subscriptions)
            if (!_resourceSubscriptionDict.ContainsKey("Encounter"))
            {
                // done
                return;
            }

            // attempt to parse the encounter
            try
            {
                if ((!string.IsNullOrEmpty(content)) && (content.Length > 20))
                {
                    // attempt to parse this encounter
                    csEncounter = JsonSerializer.Deserialize<fhirCsModels5.Encounter>(content);
                    sdkEncounter = _fhirParser.Parse<Encounter>(content);
                }
            }
            catch (Exception)
            {
                // ignore, likely an operation outcome or empty content
            }

            // check for not having an encounter
            if (csEncounter == null)
            {
                FhirClient client = null;

                // attempt to retrieve the encounter
                try
                {
                    // get a FHIR client
#pragma warning disable CA2000 // Dispose objects before losing scope
                    if (!TryGetFhirClient(out client))
#pragma warning restore CA2000 // Dispose objects before losing scope
                    {
                        return;
                    }

                    // read the specified resource
                    sdkEncounter = client.Read<Encounter>(location);

                    // convert to local object (needed for notifications)
                    csEncounter = System.Text.Json.JsonSerializer.Deserialize<fhirCsModels5.Encounter>(
                        _fhirSerializer.SerializeToString(sdkEncounter));
                }
                finally
                {
                    // return the client
                    ReturnFhirClientToQ(ref client);
                }
            }

            // check for no resource
            if (csEncounter == null)
            {
                Console.WriteLine("Could not get Encounter resource!");
                return;
            }

            // parse the encounter
            try
            {
                // build a list of all our keys
                List<string> searchKeys = new ();

                // add our patient match key
                searchKeys.Add($"patient:{sdkEncounter.Subject.Reference}");

                // get the groups this patient belongs to
                GetEncounterPatientGroupKeys(sdkEncounter, out List<string> patientGroupKeys);

                // add our patient group keys
                searchKeys.AddRange(patientGroupKeys);

                // sort our keys
                searchKeys.Sort();

                // create a hashset of the keys (for exclusion checking)
                HashSet<string> searchSet = searchKeys.ToHashSet<string>();

                // find the subscriptions for this resource
                FindSubscriptionsForKeys(
                    _resourceSubscriptionDict["Encounter"],
                    searchKeys,
                    searchSet,
                    ref subscriptions);

                // notify all subscriptions
                foreach (fhirCsModels5.Subscription subscription in subscriptions)
                {
                    _ = TryNotifySubscription(subscription.Id, csEncounter);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProcessEncounter <<< caught exception: {ex.Message}");
            }
        }

        /// <summary>Searches for the first subscriptions for keys.</summary>
        /// <param name="node">         The node.</param>
        /// <param name="matchKeys">    The match keys.</param>
        /// <param name="matchHashSet"> Set the match hash belongs to.</param>
        /// <param name="subscriptions">[in,out] The subscriptions.</param>
        private void FindSubscriptionsForKeys(
            SubscriptionFilterNode node,
            List<string> matchKeys,
            HashSet<string> matchHashSet,
            ref List<fhirCsModels5.Subscription> subscriptions)
        {
            // add subscriptions on this level
            if (node.SubscriptionsR5.Count > 0)
            {
                List<fhirCsModels5.Subscription> subscriptionsToRemove = new ();
                foreach (fhirCsModels5.Subscription subscription in node.SubscriptionsR5)
                {
                    if (!_idSubscriptionDict.ContainsKey(subscription.Id))
                    {
                        subscriptionsToRemove.Add(subscription);
                        continue;
                    }

                    subscriptions.Add(_idSubscriptionDict[subscription.Id]);
                }

                if (subscriptionsToRemove.Any())
                {
                    foreach (fhirCsModels5.Subscription subscription in subscriptionsToRemove)
                    {
                        node.SubscriptionsR5.Remove(subscription);
                    }
                }

            }

            // check each of our keys
            foreach (string key in matchKeys)
            {
                // check for inclusions
                if (node.Inclusions.ContainsKey(key))
                {
                    // **** process this node ***
                    FindSubscriptionsForKeys(node.Inclusions[key], matchKeys, matchHashSet, ref subscriptions);
                }
            }

            // check exclusions
            foreach (string key in node.Exclusions.Keys.ToArray<string>())
            {
                // check for not existing
                if (!matchHashSet.Contains(key))
                {
                    // process this node
                    FindSubscriptionsForKeys(node.Exclusions[key], matchKeys, matchHashSet, ref subscriptions);
                }
            }
        }

        /// <summary>Handles a POST of a Subscription object.</summary>
        /// <param name="content">       The content.</param>
        /// <param name="subscription">  [out] The subscription.</param>
        /// <param name="statusCode">    [out] The status code.</param>
        /// <param name="failureContent">[out] The failure content.</param>
        private void _HandlePost(
            string content,
            out fhirCsModels5.Subscription subscription,
            out HttpStatusCode statusCode,
            out string failureContent)
        {
            subscription = null;
            statusCode = System.Net.HttpStatusCode.Created;
            failureContent = string.Empty;

            // attempt to parse our content into a subscription request
            try
            {
                subscription = JsonSerializer.Deserialize<fhirCsModels5.Subscription>(content);

                // check for no parsed object
                if (subscription == null)
                {
                    statusCode = HttpStatusCode.BadRequest;
                    failureContent = "Invalid subscription";
                    return;
                }

                // check for no channel
                if (subscription.ChannelType == null)
                {
                    statusCode = HttpStatusCode.BadRequest;
                    failureContent = "Invalid channel";
                    return;
                }

                // check for invalid content type
                if (string.IsNullOrEmpty(subscription.ContentType) ||
                    subscription.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                {
                    statusCode = HttpStatusCode.BadRequest;
                    failureContent = $"Invalid channel payload type: {subscription.ContentType}!" +
                        $" currently only 'application/fhir+json' is supported.";
                    return;
                }

                // check for the topic
                if ((subscription.Topic == null) ||
                    (!SubscriptionTopicManagerR5.IsImplemented(subscription.Topic)))
                {
                    statusCode = HttpStatusCode.BadRequest;
                    failureContent = $"Invalid SubscriptionTopic: {subscription.Topic}!";
                    return;
                }

                if (string.IsNullOrEmpty(subscription.Id))
                {
                    subscription.Id = Guid.NewGuid().ToString();
                }

                // force the subscription to be one day or less
                DateTime maxEnd = DateTime.Now.AddDays(1.0);

                if ((subscription.End == null) ||
                    (!DateTime.TryParse(subscription.End, out DateTime end)) ||
                    (end > maxEnd))
                {
                    // force our end
                    subscription.End = maxEnd.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK");
                }

                // make sure we are requested
                subscription.Status = fhirCsModels5.SubscriptionStatusCodes.REQUESTED;

                _AddOrUpdate(subscription);

                bool shouldSendHandshake = false;

                string expandedChannelType = string.Empty;

                if (subscription.ChannelType != null)
                {
                    expandedChannelType = string.IsNullOrEmpty(subscription.ChannelType.System)
                        ? subscription.ChannelType.Code
                        : subscription.ChannelType.System + "#" + subscription.ChannelType.Code;
                }

                if (string.IsNullOrEmpty(expandedChannelType))
                {
                    Console.WriteLine($"HandlePost <<< Invalid subscription channel type!");
                    statusCode = HttpStatusCode.BadRequest;
                    failureContent = $"Channel Type is required for subscriptions!";
                    return;
                }

                switch (expandedChannelType)
                {
                    case ZulipExtensions.ZulipChannelUrl:
                        shouldSendHandshake = true;
                        break;

                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralRestHook:
                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralSubscriptionChannelTypeRestHook:

                        if (string.IsNullOrEmpty(subscription.Endpoint) ||
                            (!Uri.TryCreate(subscription.Endpoint, UriKind.Absolute, out _)))
                        {
                            statusCode = HttpStatusCode.BadRequest;
                            failureContent = $"Invalid Endpoint for rest-hook subscription: {subscription.Endpoint}";
                            return;
                        }

                        // rest-hook sends handshake
                        shouldSendHandshake = true;
                        break;

                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralEmail:
                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralSubscriptionChannelTypeEmail:
                        // email sends handshake
                        shouldSendHandshake = true;
                        break;

                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralWebsocket:
                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralSubscriptionChannelTypeWebsocket:
                        // websocket does NOT send handshake
                        shouldSendHandshake = false;
                        break;

                    default:
                        statusCode = HttpStatusCode.BadRequest;
                        failureContent = $"Invalid subscription channel type requested: {subscription.ChannelType.System}#{subscription.ChannelType.Code}";
                        return;
                }

                if (shouldSendHandshake)
                {
                    string id = subscription.Id;

                    // attempt to validate the endpoint
                    _ = System.Threading.Tasks.Task.Run((Action)(() => HandshakeSubscription(id)));
                }
                else
                {
                    // if there's no handshake, just mark the subscription active
                    _idSubscriptionDict[subscription.Id].Status = fhirCsModels5.SubscriptionStatusCodes.ACTIVE;
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Console.WriteLine($"SubscriptionManager._HandlePost <<< caught exception: {ex}");
            }
        }

        /// <summary>Adds or updates a Topic.</summary>
        /// <param name="subscription">The subscription.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool _AddOrUpdate(fhirCsModels5.Subscription subscription)
        {
            // check for an existing subscription (may need to remove URL for cleanup)
            if (_idSubscriptionDict.ContainsKey(subscription.Id))
            {
                // remove from the main dict
                _idSubscriptionDict.Remove(subscription.Id);
            }

            if (_subscriptionEventCache.ContainsKey(subscription.Id))
            {
                _subscriptionEventCache.Remove(subscription.Id);
            }

            if (_idEventCountDict.ContainsKey(subscription.Id))
            {
                _idEventCountDict.Remove(subscription.Id);
            }

            if (_idLastEventTicksDict.ContainsKey(subscription.Id))
            {
                _idLastEventTicksDict.Remove(subscription.Id);
            }

            // create a lock object for this subscription
            if (!_idLockDict.ContainsKey(subscription.Id))
            {
                _idLockDict.Add(subscription.Id, new object());
            }

            // add to the main dictionaries
            _idSubscriptionDict.Add(subscription.Id, subscription);
            _subscriptionEventCache.Add(subscription.Id, new ());
            _idEventCountDict.Add(subscription.Id, 0);
            _idLastEventTicksDict.Add(subscription.Id, DateTime.Now.Ticks);

            // log this addition
            Console.WriteLine($" <<< added Subscription:" +
                $" {subscription.Id}" +
                $" ({subscription.ChannelType.Code}," +
                $" {subscription.Content})");

            // get the topic for this subscription
            fhirCsR5.Models.SubscriptionTopic topic = SubscriptionTopicManagerR5.GetTopic(
                Program.ResourceIdFromReference(subscription.Topic));

            // check for unknown topic
            if (topic == null)
            {
                Console.WriteLine($"SubscriptionManager._AddOrUpdate <<< could not find topic: {subscription.Topic}");
                return false;
            }

            // track this subscription (based on topic)
            if (!TrackSubscription(subscription, topic))
            {
                // make sure partial updates are removed
                Remove(subscription.Id);

                // failed
                return false;
            }

            // still here is success
            return true;
        }

        /// <summary>Track resource.</summary>
        /// <param name="resourceName">Name of the resource.</param>
        private void TrackResource(string resourceName)
        {
            lock (_resourceSubscriptionDictLock)
            {
                // make sure this resource is tracked
                if (!_resourceSubscriptionDict.ContainsKey(resourceName))
                {
                    _resourceSubscriptionDict.Add(resourceName, new SubscriptionFilterNode()
                    {
                        SubscriptionsR5 = new (),
                        Inclusions = new (),
                        Exclusions = new (),
                    })
                    ;
                }
            }
        }

        /// <summary>Track subscription.</summary>
        /// <param name="subscription">[out] The subscription.</param>
        /// <param name="topic">       The topic.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool TrackSubscription(
            fhirCsModels5.Subscription subscription,
            fhirCsR5.Models.SubscriptionTopic topic)
        {
            // check for unfiltered subscriptions
            if ((subscription.FilterBy == null) || (!subscription.FilterBy.Any()))
            {
                // check for resource types
                if (!topic.ResourceTrigger.Any())
                {
                    // reject this subscription
                    Console.WriteLine("SubscriptionManager.TrackSubscription <<< invalid resource triggers: [].");
                    return false;
                }
                else
                {
                    // loop over resource types
                    foreach (fhirCsR5.Models.SubscriptionTopicResourceTrigger resourceTrigger in topic.ResourceTrigger)
                    {
                        if (string.IsNullOrEmpty(resourceTrigger.Resource))
                        {
                            continue;
                        }

                        TrackResource(resourceTrigger.Resource);
                        lock (_resourceSubscriptionDictLock)
                        {
                            _resourceSubscriptionDict[resourceTrigger.Resource].SubscriptionsR5.Add(subscription);
                        }
                    }
                }

                // done with this type of subscription
                return true;
            }

            // for now, reject any subscription that starts with an exclusion
            if (subscription.FilterBy[0].SearchModifier == fhirCsModels5.SubscriptionFilterBySearchModifierCodes.NOT_IN)
            {
                Console.WriteLine($"SubscriptionManager.TrackSubscription <<< first match cannot be {subscription.FilterBy[0].SearchModifier}");
                return false;
            }

            // get our list of filters
            List<fhirCsModels5.SubscriptionFilterBy> filters = subscription.FilterBy.ToList();

            // sort by field, match, value
            filters.Sort(
                (a, b) => string.CompareOrdinal(
                    $"{a.SearchParamName}{a.SearchModifier}{a.Value}",
                    $"{b.SearchParamName}{b.SearchModifier}{b.Value}"));

            // loop over resources in the topic
            foreach (fhirCsR5.Models.SubscriptionTopicResourceTrigger resourceTrigger in topic.ResourceTrigger)
            {
                if (string.IsNullOrEmpty(resourceTrigger.Resource))
                {
                    continue;
                }

                // make sure this resource is tracked
                TrackResource(resourceTrigger.Resource);

                // track based on filters
                if (!TrackFilterNode(subscription, _resourceSubscriptionDict[resourceTrigger.Resource], filters))
                {
                    return false;
                }
            }

            // still here is success
            return true;
        }

        /// <summary>Track filter node.</summary>
        /// <param name="subscription">[out] The subscription.</param>
        /// <param name="node">        The node.</param>
        /// <param name="filters">     The filters.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool TrackFilterNode(
            fhirCsModels5.Subscription subscription,
            SubscriptionFilterNode node,
            List<fhirCsModels5.SubscriptionFilterBy> filters)
        {
            // check for no filters (done)
            if (filters.Count == 0)
            {
                // add the subscription to this node
                node.SubscriptionsR5.Add(subscription);

                // done
                return true;
            }

            // grab the first filter (sorted)
            fhirCsModels5.SubscriptionFilterBy filter = filters[0];
            filters.RemoveAt(0);

            // check for no value
            if (string.IsNullOrEmpty(filter.Value))
            {
                // cannot add this
                Console.WriteLine($"SubscriptionManager.TrackFilterNode <<<" +
                    $" invalid value is null! subscription: {subscription.Id}");
                return false;
            }

            bool success = true;

            // split possible values
            string[] filterValues = filter.Value.Split(',');

            foreach (string filterValue in filterValues)
            {
                // build the key
                string filterKey = $"{filter.SearchParamName}:{filterValue}";

                // add to the correct type
                switch (filter.SearchModifier)
                {
                    case fhirCsModels5.SubscriptionFilterBySearchModifierCodes.NOT_IN:
                    case fhirCsModels5.SubscriptionFilterBySearchModifierCodes.NE:
                        // make sure this field is in exclusions
                        lock (_resourceSubscriptionDictLock)
                        {
                            if (!node.Exclusions.ContainsKey(filterKey))
                            {
                                node.Exclusions.Add(filterKey, new SubscriptionFilterNode()
                                {
                                    SubscriptionsR5 = new (),
                                    Inclusions = new (),
                                    Exclusions = new (),
                                });
                            }
                        }

                        // continue traversing, stop on failures
                        if (!TrackFilterNode(subscription, node.Exclusions[filterKey], filters))
                        {
                            return false;
                        }

                        break;

                    case fhirCsModels5.SubscriptionFilterBySearchModifierCodes.VAL_IN:
                    case fhirCsModels5.SubscriptionFilterBySearchModifierCodes.EQUALS:
                    case fhirCsModels5.SubscriptionFilterBySearchModifierCodes.EQ:
                        // make sure this field is in the inclusions
                        lock (_resourceSubscriptionDictLock)
                        {
                            if (!node.Inclusions.ContainsKey(filterKey))
                            {
                                node.Inclusions.Add(filterKey, new SubscriptionFilterNode()
                                {
                                    SubscriptionsR5 = new (),
                                    Inclusions = new (),
                                    Exclusions = new (),
                                });
                            }
                        }

                        // continue traversing, stop of failures
                        if (!TrackFilterNode(subscription, node.Inclusions[filterKey], filters))
                        {
                            return false;
                        }

                        break;

                    default:
                        // unhandled match type
                        Console.WriteLine($"SubscriptionManager.TrackFilterNode <<<" +
                            $" invalid MatchType: {filter.SearchModifier}! subscription: {subscription.Id}");
                        return false;
                }
            }

            return success;
        }

        /// <summary>Dumps a node.</summary>
        /// <param name="node">The node.</param>
        private void DumpNode(ISourceNode node)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,70} {1,20} {2}", "Location", "Name", "Text"));
            _DumpNode(node);
        }

        /// <summary>Dumps a node.</summary>
        /// <param name="node">The node.</param>
        private void _DumpNode(ISourceNode node)
        {
            Console.WriteLine($"{node.Location,70} {node.Name,20} {node.Text}");

            foreach (ISourceNode child in node.Children())
            {
                _DumpNode(child);
            }
        }

        /// <summary>Handshake REST hook.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <returns>An asynchronous result that yields true if it succeeds, false if it fails.</returns>
        private bool HandshakeSubscription(string subscriptionId)
        {
            // sanity checks
            if (string.IsNullOrEmpty(subscriptionId) ||
                (!_idSubscriptionDict.ContainsKey(subscriptionId)))
            {
                return false;
            }

            fhirCsModels5.Subscription subscription = _idSubscriptionDict[subscriptionId];

            if (string.IsNullOrEmpty(subscription.Endpoint))
            {
                // nothing to do
                _idSubscriptionDict[subscription.Id].Status = fhirCsModels5.SubscriptionStatusCodes.ERROR;

                return false;
            }

            bool notified = TryNotifySubscription(subscriptionId, null, true);

            // attempt to send the notification
            if (notified)
            {
                // update in the manager (if it hasn't been removed)
                if (_idSubscriptionDict.ContainsKey(subscriptionId))
                {
                    _idSubscriptionDict[subscriptionId].Status = fhirCsModels5.SubscriptionStatusCodes.ACTIVE;
                }
            }
            else
            {
                // update to error in the manager (if it hasn't been removed)
                if (_idSubscriptionDict.ContainsKey(subscriptionId))
                {
                    _idSubscriptionDict[subscriptionId].Status = fhirCsModels5.SubscriptionStatusCodes.ERROR;
                }
            }

            // tell the user
            Console.WriteLine($" <<< Subscription {subscription.Id} set to active!");

            // done
            return true;
        }

        /// <summary>Attempts to get fhir client a FhirClient from the given string.</summary>
        /// <param name="client">[out] The client.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool TryGetFhirClient(out FhirClient client)
        {
            // check to see if we can get a client from the queue
            lock (_clientQLock)
            {
                if (_clientQ.Count > 0)
                {
                    client = _clientQ.Dequeue();
                    return true;
                }

                // attempt to create our client
                try
                {
                    client = new FhirClient(
                        Program.FhirServerUrlR5,
                        new Hl7.Fhir.Rest.FhirClientSettings()
                        {
                            PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Json,
                            PreferredReturn = Hl7.Fhir.Rest.Prefer.ReturnRepresentation,
                        });

                    // valid
                    return true;
                }
                catch (Exception ex)
                {
                    // log for reference
                    Console.WriteLine($"SubscriptionMananger.TryGetFhirClient <<<" +
                        $" failed url: {Program.FhirServerUrlR5}," +
                        $" exception: {ex.Message}");
                }
            }

            // still here means failure
            client = null;
            return false;
        }

        /// <summary>Returns fhir client to q.</summary>
        /// <param name="client">[out] The client.</param>
        private void ReturnFhirClientToQ(ref FhirClient client)
        {
            // thread safe
            lock (_clientQLock)
            {
                _clientQ.Enqueue(client);
                client = null;
            }
        }

        /// <summary>
        /// Attempts to get subscription status the SubscriptionStatus from the given Subscription.
        /// </summary>
        /// <param name="id">                  The identifier.</param>
        /// <param name="status">              [out] The status.</param>
        /// <param name="eventsInNotification">The events in notification.</param>
        /// <param name="isForQuery">          True if is for query, false if not.</param>
        /// <param name="isHandshake">         True if is handshake, false if not.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetSubscriptionStatus(
            string id,
            out fhirCsModels5.SubscriptionStatus status,
            int eventsInNotification,
            bool isForQuery,
            bool isHandshake)
        {
            if (string.IsNullOrEmpty(id))
            {
                status = null;
                return false;
            }

            if (!TryGetSubscription(id, out fhirCsModels5.Subscription subscription))
            {
                status = null;
                return false;
            }

            long eventCount = _instance._idEventCountDict[subscription.Id];

            status = new fhirCsModels5.SubscriptionStatus()
            {
                EventsSinceSubscriptionStart = eventCount,
                EventsInNotification = eventsInNotification,
                Status = subscription.Status.ToString().ToLowerInvariant(),
                Subscription = new fhirCsModels5.Reference()
                {
                    ReferenceField = Program.UrlForR5ResourceId("Subscription", subscription.Id),
                },
                Topic = subscription.Topic,
            };

            if (isForQuery)
            {
                status.Type = fhirCsValueSets5.SubscriptionNotificationTypeCodes.LiteralQueryStatus;
            }
            else if (eventsInNotification == 0)
            {
                if (isHandshake)
                {
                    status.Type = fhirCsValueSets5.SubscriptionNotificationTypeCodes.LiteralHandshake;
                }
                else
                {
                    status.Type = fhirCsValueSets5.SubscriptionNotificationTypeCodes.LiteralHeartbeat;
                }
            }
            else
            {
                status.Type = fhirCsValueSets5.SubscriptionNotificationTypeCodes.LiteralEventNotification;
            }

            return true;
        }

        /// <summary>Bundle for subscription notification.</summary>
        /// <exception cref="ArgumentNullException">Thrown when one or more required arguments are null.</exception>
        /// <param name="subscription">          [out] The subscription.</param>
        /// <param name="content">               The content.</param>
        /// <param name="isHandshake">           True if is handshake, false if not.</param>
        /// <param name="bundle">                [out] The bundle.</param>
        /// <param name="subscriptionEventCount">[out] Number of subscription events.</param>
        /// <param name="cachedNotification">    [out] The cached notification.</param>
        public static void BundleForSubscriptionNotification(
            fhirCsModels5.Subscription subscription,
            fhirCsModels5.Resource content,
            bool isHandshake,
            out fhirCsModels5.Bundle bundle,
            out long subscriptionEventCount,
            out CachedNotificationEvent cachedNotification)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            cachedNotification = null;

            // check our event count
            lock (_instance._idLockDict[subscription.Id])
            {
                // get the event count
                subscriptionEventCount = _instance._idEventCountDict[subscription.Id];

                // check if we are incrementing the event
                if (content != null)
                {
                    _instance._idEventCountDict[subscription.Id] = ++subscriptionEventCount;
                }
            }

            // create a bundle for this message message
            bundle = new fhirCsModels5.Bundle()
            {
                Type = fhirCsModels5.BundleTypeCodes.SUBSCRIPTION_NOTIFICATION,
                Timestamp = DateTimeOffset.Now.ToUniversalTime(),
                Entry = new List<fhirCsModels5.BundleEntry>(),
            };

            if (TryGetSubscriptionStatus(
                    subscription.Id,
                    out fhirCsModels5.SubscriptionStatus status,
                    (content == null) ? 0 : 1,
                    false,
                    isHandshake))
            {
                string url = Program.UrlForR5ResourceId("Subscription", subscription.Id) + "/$status";
                bundle.Entry.Add(new fhirCsModels5.BundleEntry()
                {
                    FullUrl = url,
                    Request = new fhirCsModels5.BundleEntryRequest()
                    {
                        Method = "GET",
                        Url = url,
                    },
                    Response = new fhirCsModels5.BundleEntryResponse()
                    {
                        Status = "200",
                    },
                });
            }

            // check if we are adding contents
            if ((content != null) && (subscription.Content != fhirCsModels5.SubscriptionContentCodes.EMPTY))
            {
                string url = Program.UrlForR5ResourceId(content.ResourceType, content.Id);

                switch (subscription.Content)
                {
                    case fhirCsModels5.SubscriptionContentCodes.EMPTY:
                        break;

                    case fhirCsModels5.SubscriptionContentCodes.ID_ONLY:
                        // add the URL, but no resource
                        bundle.Entry.Add(new fhirCsModels5.BundleEntry()
                        {
                            FullUrl = url,
                            Request = new fhirCsModels5.BundleEntryRequest()
                            {
                                Method = "POST",
                                Url = url,
                            },
                            Response = new fhirCsModels5.BundleEntryResponse()
                            {
                                Status = "204",
                            },
                        });

                        status.NotificationEvent = new List<fhirCsModels5.SubscriptionStatusNotificationEvent>()
                        {
                            new fhirCsModels5.SubscriptionStatusNotificationEvent()
                            {
                                EventNumber = subscriptionEventCount,
                                Focus = new fhirCsModels5.Reference()
                                {
                                    ReferenceField = url,
                                },
                            },
                        };
                        break;

                    case fhirCsModels5.SubscriptionContentCodes.FULL_RESOURCE:
                        // add the URL and the resource
                        bundle.Entry.Add(new fhirCsModels5.BundleEntry()
                        {
                            FullUrl = url,
                            Request = new fhirCsModels5.BundleEntryRequest()
                            {
                                Method = "POST",
                                Url = url,
                            },
                            Response = new fhirCsModels5.BundleEntryResponse()
                            {
                                Status = "204",
                            },
                            Resource = content,
                        });

                        status.NotificationEvent = new List<fhirCsModels5.SubscriptionStatusNotificationEvent>()
                        {
                            new fhirCsModels5.SubscriptionStatusNotificationEvent()
                            {
                                EventNumber = subscriptionEventCount,
                                Focus = new fhirCsModels5.Reference()
                                {
                                    ReferenceField = url,
                                },
                            },
                        };
                        break;
                }

                // check for related resources and add them
                if (TryGetRelatedResources(
                        subscription,
                        content,
                        subscriptionEventCount,
                        ref bundle,
                        out cachedNotification))
                {
                    if ((status.NotificationEvent != null) &&
                        status.NotificationEvent.Any() &&
                        (cachedNotification != null) &&
                        (cachedNotification.AdditionalR5 != null) &&
                        cachedNotification.AdditionalR5.Any())
                    {
                        status.NotificationEvent[0].AdditionalContext = new List<fhirCsModels5.Reference>();

                        foreach (string additional in cachedNotification.AdditionalR5.Keys)
                        {
                            status.NotificationEvent[0].AdditionalContext.Add(new fhirCsModels5.Reference()
                            {
                                ReferenceField = additional,
                            });
                        }
                    }
                }
            }

            bundle.Entry[0].Resource = status;
        }

        /// <summary>Attempts to get related resources.</summary>
        /// <param name="subscription">      [out] The subscription.</param>
        /// <param name="content">           The content.</param>
        /// <param name="eventNumber">       The event number.</param>
        /// <param name="bundle">            [out] The bundle.</param>
        /// <param name="cachedNotification">[out] The cached notification.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private static bool TryGetRelatedResources(
            fhirCsModels5.Subscription subscription,
            fhirCsModels5.Resource content,
            long eventNumber,
            ref fhirCsModels5.Bundle bundle,
            out CachedNotificationEvent cachedNotification)
        {
            if (content == null)
            {
                cachedNotification = null;
                return true;
            }

            FhirClient client = null;

            // get the topic
            string topicUrl = subscription.Topic;

            if (string.IsNullOrEmpty(topicUrl))
            {
                cachedNotification = null;
                return false;
            }

            cachedNotification = new CachedNotificationEvent()
            {
                Focus = Program.UrlForR5ResourceId(content.ResourceType, content.Id),
                FocusR5 = content,
            };

            List<string> includeDirectives = new List<string>();

            SubscriptionTopicManagerR5.TryGetTopic(topicUrl, out fhirCsModels5.SubscriptionTopic topic);

            if ((topic.NotificationShape == null) || (!topic.NotificationShape.Any()))
            {
                return true;
            }

            foreach (fhirCsModels5.SubscriptionTopicNotificationShape shape in topic.NotificationShape)
            {
                if (shape.Resource != content.ResourceType)
                {
                    continue;
                }

                includeDirectives.AddRange(shape.Include);
            }

            if (!includeDirectives.Any())
            {
                return true;
            }

            // attempt to get additional data
            try
            {
                // get a FHIR client
#pragma warning disable CA2000 // Dispose objects before losing scope
                if (!_instance.TryGetFhirClient(out client))
#pragma warning restore CA2000 // Dispose objects before losing scope
                {
                    return false;
                }

                // ask for the resource, plus our includes
                Bundle results = client.Search(
                    content.ResourceType,
                    new string[] { $"_id={content.Id}" },
                    includeDirectives.ToArray());

                if ((results == null) ||
                    (results.Entry == null) ||
                    (results.Entry.Count == 0))
                {
                    return true;
                }

                foreach (Bundle.EntryComponent entry in results.Entry)
                {
                    if ((entry.Resource.TypeName == content.ResourceType) &&
                        (entry.Resource.Id == content.Id))
                    {
                        continue;
                    }

                    string json = _instance._fhirSerializer.SerializeToString(entry.Resource);
                    fhirCsModels5.Resource res = JsonSerializer.Deserialize<fhirCsModels5.Resource>(json);

                    string fullUrl = Program.UrlForR5ResourceId(entry.Resource.TypeName, entry.Resource.Id);
                    cachedNotification.AdditionalR5.Add(fullUrl, res);

                    switch (subscription.Content)
                    {
                        case BackportedSubscription.ContentCodeEmpty:
                            break;

                        case BackportedSubscription.ContentCodeIdOnly:
                            bundle.Entry.Add(new fhirCsModels5.BundleEntry()
                            {
                                FullUrl = fullUrl,
                                Request = new fhirCsModels5.BundleEntryRequest()
                                {
                                    Method = "GET",
                                    Url = fullUrl,
                                },
                                Response = new fhirCsModels5.BundleEntryResponse()
                                {
                                    Status = "200",
                                },
                            });

                            break;

                        case BackportedSubscription.ContentCodeFullResource:
                            bundle.Entry.Add(new fhirCsModels5.BundleEntry()
                            {
                                FullUrl = fullUrl,
                                Request = new fhirCsModels5.BundleEntryRequest()
                                {
                                    Method = "GET",
                                    Url = fullUrl,
                                },
                                Response = new fhirCsModels5.BundleEntryResponse()
                                {
                                    Status = "200",
                                },
                                Resource = res,
                            });
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TryGetRelatedResources <<< caught exception: {ex.Message}");
            }
            finally
            {
                if (client != null)
                {
                    // return to our queue
                    _instance.ReturnFhirClientToQ(ref client);
                }
            }

            return true;
        }

        /// <summary>Error concept for string.</summary>
        /// <param name="message">The message.</param>
        /// <param name="errno">  (Optional) The errno.</param>
        /// <returns>A CodeableConcept[].</returns>
        private static List<CodeableConcept> ErrorConceptForString(string message, int errno = 1)
        {
            return new List<CodeableConcept>()
            {
                new CodeableConcept(
                    "http://example.org/primary/code/system/is/not/yet/defined",
                    $"{errno}",
                    message),
            };
        }

        /// <summary>Attempts to get bundle for events.</summary>
        /// <param name="subscriptionId"> The subscription id.</param>
        /// <param name="eventNumberLow"> The event number low.</param>
        /// <param name="eventNumberHigh">The event number high.</param>
        /// <param name="contentHint">    The content hint.</param>
        /// <param name="bundle">         [out] The bundle.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetBundleForEvents(
            string subscriptionId,
            long eventNumberLow,
            long eventNumberHigh,
            string contentHint,
            out fhirCsModels5.Bundle bundle)
        {
            return _instance._TryGetBundleForEvents(
                subscriptionId,
                eventNumberLow,
                eventNumberHigh,
                contentHint,
                out bundle);
        }

        /// <summary>Attempts to get bundle for events.</summary>
        /// <param name="subscriptionId"> The subscription id.</param>
        /// <param name="eventNumberLow"> The event number low.</param>
        /// <param name="eventNumberHigh">The event number high.</param>
        /// <param name="contentHint">    The content hint.</param>
        /// <param name="bundle">         [out] The bundle.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool _TryGetBundleForEvents(
            string subscriptionId,
            long eventNumberLow,
            long eventNumberHigh,
            string contentHint,
            out fhirCsModels5.Bundle bundle)
        {
            // sanity checks
            if (string.IsNullOrEmpty(subscriptionId) ||
                (!_idSubscriptionDict.ContainsKey(subscriptionId)))
            {
                bundle = null;
                return false;
            }

            fhirCsModels5.Subscription subscription = _idSubscriptionDict[subscriptionId];

            long low = (eventNumberLow < 1) ? 1 : eventNumberLow;
            long high = ((eventNumberHigh < 0) || (eventNumberHigh > _idEventCountDict[subscriptionId]))
                ? _idEventCountDict[subscriptionId]
                : eventNumberHigh;

            if (low > high)
            {
                long temp = high;
                high = low;
                low = temp;
            }

            int eventsIncluded = 0;

            fhirCsModels5.SubscriptionStatus status = new ()
            {
                EventsSinceSubscriptionStart = _idEventCountDict[subscriptionId],
                EventsInNotification = 0,
                Status = subscription.Status.ToString().ToLowerInvariant(),
                Type = fhirCsValueSets5.SubscriptionNotificationTypeCodes.LiteralQueryEvent,
                Subscription = new ()
                {
                    ReferenceField = Program.UrlForR5ResourceId("Subscription", subscription.Id),
                },
                Topic = subscription.Topic,
                NotificationEvent = new (),
            };

            // create a bundle for this message message
            bundle = new ()
            {
                Type = fhirCsModels5.BundleTypeCodes.SUBSCRIPTION_NOTIFICATION,
                Timestamp = DateTimeOffset.Now.ToUniversalTime(),
                Entry = new (),
            };

            bundle.Entry.Add(new ()
            {
                FullUrl = Program.UrlForR5ResourceId("Subscription", subscription.Id) + "/$events",
                Request = new ()
                {
                    Method = "GET",
                    Url = Program.UrlForR5ResourceId("Subscription", subscription.Id) + "/$events",
                },
                Response = new ()
                {
                    Status = "200",
                },
            });

            // if there's no content hint, use the subscription style
            if (string.IsNullOrEmpty(contentHint))
            {
                contentHint = subscription.Content;

                // if we still don't have a content type, use id-only
                if (string.IsNullOrEmpty(contentHint))
                {
                    contentHint = fhirCsModels5.SubscriptionContentCodes.ID_ONLY;
                }
            }

            HashSet<string> addedContents = new ();

            for (long i = low; i <= high; i++)
            {
                if (!_subscriptionEventCache[subscriptionId].ContainsKey(i))
                {
                    continue;
                }

                eventsIncluded++;

                fhirCsModels5.SubscriptionStatusNotificationEvent statusEvent = new ()
                {
                    EventNumber = i,
                };

                statusEvent.Focus = new ()
                {
                    ReferenceField = _subscriptionEventCache[subscriptionId][i].Focus,
                };

                if (!addedContents.Contains(_subscriptionEventCache[subscriptionId][i].Focus))
                {
                    addedContents.Add(_subscriptionEventCache[subscriptionId][i].Focus);

                    bundle.Entry.Add(new ()
                    {
                        FullUrl = _subscriptionEventCache[subscriptionId][i].Focus,
                        Request = new ()
                        {
                            Method = "POST",
                            Url = _subscriptionEventCache[subscriptionId][i].Focus,
                        },
                        Response = new ()
                        {
                            Status = "204",
                        },
                        Resource = (contentHint == fhirCsModels5.SubscriptionContentCodes.FULL_RESOURCE)
                            ? _subscriptionEventCache[subscriptionId][i].FocusR5
                            : null,
                    });
                }

                if (_subscriptionEventCache[subscriptionId][i].AdditionalR5.Count > 0)
                {
                    statusEvent.AdditionalContext = new ();
                }

                foreach (KeyValuePair<string, fhirCsModels5.Resource> kvp in _subscriptionEventCache[subscriptionId][i].AdditionalR5)
                {
                    statusEvent.AdditionalContext.Add(new fhirCsModels5.Reference()
                    {
                        ReferenceField = kvp.Key,
                    });

                    if (!addedContents.Contains(kvp.Key))
                    {
                        addedContents.Add(kvp.Key);

                        bundle.Entry.Add(new ()
                        {
                            FullUrl = kvp.Key,
                            Request = new ()
                            {
                                Method = "GET",
                                Url = kvp.Key,
                            },
                            Response = new ()
                            {
                                Status = "200",
                            },
                            Resource = (contentHint == fhirCsModels5.SubscriptionContentCodes.FULL_RESOURCE)
                                ? kvp.Value
                                : null,
                        });
                    }
                }

                status.NotificationEvent.Add(statusEvent);
            }

            status.EventsInNotification = eventsIncluded;

            // add the contents of our SubscriptionStatus
            bundle.Entry[0].Resource = status;

            return true;
        }

        /// <summary>Attempts to notify subscription a Resource from the given string.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="content">       (Optional) The content.</param>
        /// <param name="isHandshake">   (Optional) True if is handshake, false if not.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool TryNotifySubscription(
            string subscriptionId,
            fhirCsModels5.Resource content = null,
            bool isHandshake = false)
        {
            try
            {
                // sanity checks
                if (string.IsNullOrEmpty(subscriptionId) ||
                    (!_idSubscriptionDict.ContainsKey(subscriptionId)))
                {
                    // fail
                    return false;
                }

                _idLastEventTicksDict[subscriptionId] = DateTime.Now.Ticks;

                string contentTypeName = (content == null) ? string.Empty : content.ResourceType;
                string contentId = (content == null) ? string.Empty : content.Id;

                // get the subscription
                fhirCsModels5.Subscription subscription = _idSubscriptionDict[subscriptionId];

                // get our bundle we want to send
                BundleForSubscriptionNotification(
                    subscription,
                    content,
                    isHandshake,
                    out fhirCsModels5.Bundle bundle,
                    out long subscriptionEventCount,
                    out CachedNotificationEvent cacheNotification);

                Console.WriteLine($"SubscriptionManagerR5.TryNotifySubscription <<<" +
                    $" attempting to send {subscriptionId}" +
                    $" event {_idEventCountDict[subscriptionId]}" +
                    $" via {subscription.ChannelType.System}#{subscription.ChannelType.Code}...");

                string json = JsonSerializer.Serialize(bundle);

                if (cacheNotification != null)
                {
                    _subscriptionEventCache[subscriptionId].Add(subscriptionEventCount, cacheNotification);
                    if (_subscriptionEventCache[subscriptionId].Count > MaxCachedEvents)
                    {
                        _subscriptionEventCache[subscriptionId].Remove(_subscriptionEventCache[subscriptionId].Keys.Min());
                    }
                }

                string expandedChannelType = string.Empty;

                if (subscription.ChannelType != null)
                {
                    expandedChannelType = string.IsNullOrEmpty(subscription.ChannelType.System)
                        ? subscription.ChannelType.Code
                        : subscription.ChannelType.System + "#" + subscription.ChannelType.Code;
                }

                if (string.IsNullOrEmpty(expandedChannelType))
                {
                    Console.WriteLine($"TryNotifySubscription <<< Missing subscription channel type!");
                    return false;
                }

                bool notified = false;

                switch (expandedChannelType)
                {
                    case ZulipExtensions.ZulipChannelUrl:
                        {
                            bool usePm = subscription.ZulipPmUserIdTryGet(out string pmUserId);
                            bool useStream = subscription.ZulipStreamIdTryGet(out string streamId);

                            if ((!usePm) && (!useStream))
                            {
                                UpdateErrorState(subscriptionId, false);
                                return false;
                            }

                            if (!subscription.ZulipSiteTryGet(out string site))
                            {
                                site = Program.Configuration["Zulip_Site"];
                            }

                            if (!subscription.ZulipEmailTryGet(out string email))
                            {
                                email = Program.Configuration["Zulip_Email"];
                            }

                            if (!subscription.ZulipKeyTryGet(out string key))
                            {
                                key = Program.Configuration["Zulip_Key"];
                            }

                            notified = NotificationManager.TryNotifyZulip(
                                subscriptionId,
                                Program.UrlForR5ResourceId("Subscription", subscriptionId),
                                site,
                                email,
                                key,
                                streamId,
                                pmUserId,
                                subscription.ContentType,
                                subscription.Content,
                                json,
                                contentTypeName,
                                contentId,
                                Program.UrlForR5ResourceId(contentTypeName, contentId),
                                subscriptionEventCount);
                        }

                        break;

                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralRestHook:
                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralSubscriptionChannelTypeRestHook:
                        notified = NotificationManager.TryNotifyRestHook(
                                                subscription.Id,
                                                subscription.Endpoint,
                                                subscription.Header,
                                                json,
                                                contentTypeName,
                                                contentId,
                                                subscriptionEventCount);
                        break;

                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralEmail:
                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralSubscriptionChannelTypeEmail:
                        notified = NotificationManager.TryNotifyEmail(
                                                subscription.Id,
                                                subscription.Endpoint,
                                                subscription.ContentType,
                                                subscription.Content.ToString(),
                                                json,
                                                contentTypeName,
                                                contentId,
                                                subscriptionEventCount);
                        break;

                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralWebsocket:
                    case fhirCsValueSets5.SubscriptionChannelTypeCodes.LiteralSubscriptionChannelTypeWebsocket:
                        WebsocketManager.QueueMessagesForSubscription(subscription, json);
                        break;
                }

                UpdateErrorState(subscriptionId, notified);
                return notified;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubscriptionManagerR5.TryNotifySubscription <<< caught: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($" <<< inner: {ex.InnerException.Message}");
                }
            }

            // done
            return true;
        }

        /// <summary>Updates the error state.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="notified">      True if notified.</param>
        private void UpdateErrorState(
            string subscriptionId,
            bool notified)
        {
            if (notified)
            {
                // check to see if we need to clear an error
                if (_idSubscriptionDict[subscriptionId].Status == fhirCsModels5.SubscriptionStatusCodes.ERROR)
                {
                    _idSubscriptionDict[subscriptionId].Status = fhirCsModels5.SubscriptionStatusCodes.ACTIVE;
                }
            }
            else
            {
                // done
                _idSubscriptionDict[subscriptionId].Status = fhirCsModels5.SubscriptionStatusCodes.ERROR;
            }
        }

        /// <summary>Clean up thread function.</summary>
        private void CleanUpThreadFunc()
        {
            // loop forever (will be killed at shutdown as background thread)
            while (true)
            {
                // wait a minute, not in one call so we can be responsive to shutdown
                for (int sleepSecond = 0; sleepSecond < 60; sleepSecond++)
                {
                    Thread.Sleep(1000);
                }

                // check each subscription
                foreach (string id in _idLockDict.Keys)
                {
                    try
                    {
                        // check this subscription
                        fhirCsModels5.Subscription subscription = _idSubscriptionDict[id];

                        if ((subscription.End == null) ||
                            (!DateTime.TryParse(subscription.End, out DateTime end)) ||
                            (end < DateTime.Now))
                        {
                            Console.WriteLine($" <<< Removing subscription {id} due to expiration! - {DateTime.Now}");
                            Remove(id);
                        }
                    }
                    catch (Exception ex)
                    {
                        // just log for now
                        Console.WriteLine($"SubscriptionManager.CleanUpThreadFunc <<<" +
                            $"id: {id} raised exception: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>Process the heartbeats described by currentTicks.</summary>
        /// <param name="currentTicks">The current ticks.</param>
        public void ProcessHeartbeats(long currentTicks)
        {
            // don't use the subscription dict enumerator since we don't want to keep it locked so much
            List<string> subscriptionIds = _idSubscriptionDict.Keys.ToList();

            // traverse the dictionary looking for clients we need to send messages to
            foreach (string subscriptionId in subscriptionIds)
            {
                if (!_idSubscriptionDict.ContainsKey(subscriptionId))
                {
                    continue;
                }

                try
                {
                    fhirCsModels5.Subscription subscription = _idSubscriptionDict[subscriptionId];

                    if (subscription.Status != fhirCsModels5.SubscriptionStatusCodes.ACTIVE)
                    {
                        continue;
                    }

                    if ((subscription.HeartbeatPeriod == null) || (subscription.HeartbeatPeriod <= 0))
                    {
                        continue;
                    }

                    long thresholdTicks = currentTicks - TimeSpan.FromSeconds((double)subscription.HeartbeatPeriod!).Ticks;

                    // check timeout
                    if (_idLastEventTicksDict[subscriptionId] < thresholdTicks)
                    {
                        TryNotifySubscription(subscriptionId);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SubscriptionManagerR5.ProcessHeartbeats <<< caught: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($" <<< inner: {ex.InnerException.Message}");
                    }
                }
            }
        }

        /// <summary>Check or create instance.</summary>
        private static void CheckOrCreateInstance()
        {
            if (_instance == null)
            {
                _instance = new SubscriptionManagerR5();
            }
        }
    }
}
