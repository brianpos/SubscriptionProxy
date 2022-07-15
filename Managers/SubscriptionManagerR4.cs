// <copyright file="SubscriptionManagerR4.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

extern alias fhir4;
extern alias fhir5;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using argonaut_subscription_server_proxy.Backport;
using argonaut_subscription_server_proxy.Models;
using argonaut_subscription_server_proxy.Zulip;
using fhir4.Hl7.Fhir.Model;
using fhir4.Hl7.Fhir.Rest;
using fhir4.Hl7.Fhir.Serialization;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using fhirCsModels4B = fhirCsR4B.Models;
using fhirCsModels5 = fhirCsR5.Models;
using fhirCsValueSets4B = fhirCsR4B.ValueSets;

namespace argonaut_subscription_server_proxy.Managers
{
    /// <summary>Manager for subscriptions.</summary>
    public class SubscriptionManagerR4
    {
        /// <summary>The maximum cached events.</summary>
        private const int MaxCachedEvents = 10;

        /// <summary>The instance for singleton pattern.</summary>
        private static SubscriptionManagerR4 _instance;

        /// <summary>Dictionary of subscriptions, by ID.</summary>
        private Dictionary<string, fhirCsModels4B.Subscription> _idSubscriptionDict;

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
        /// Initializes a new instance of the <see cref="SubscriptionManagerR4"/> class.
        /// </summary>
        private SubscriptionManagerR4()
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
        public static SubscriptionManagerR4 Current => _instance;

        /// <summary>Initializes this object.</summary>
        public static void Init()
        {
            // make an instance
            CheckOrCreateInstance();
        }

        /// <summary>Gets a list of all currently known Subscriptions.</summary>
        /// <returns>The Subscription list.</returns>
        public static List<fhirCsModels4B.Subscription> GetSubscriptionList()
        {
            // return our list of known subscriptions
            return _instance._idSubscriptionDict.Values.ToList<fhirCsModels4B.Subscription>();
        }

        /// <summary>Gets subscriptions bundle.</summary>
        /// <param name="queryCollection">(Optional) The query collection.</param>
        /// <returns>The subscriptions bundle.</returns>
        public static fhirCsModels4B.Bundle GetSubscriptionsBundle(IQueryCollection queryCollection = null)
        {
            return _instance._GetSubscriptionsBundle(queryCollection);
        }

        /// <summary>Handles a POST of a Subscription object.</summary>
        /// <param name="content">       The content.</param>
        /// <param name="subscription">  [out] The subscription.</param>
        /// <param name="statusCode">    [out] The status code.</param>
        /// <param name="failureContent">[out] The failure content.</param>
        public static void HandlePost(
            string content,
            out fhirCsModels4B.Subscription subscription,
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
                new Uri($"/r4/Subscription/{subscriptionId}", UriKind.Relative))
                .ToString();
        }

        /// <summary>Attempts to get subscription a Subscription from the given string.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="subscription">  [out] The subscription.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetSubscription(string subscriptionId, out fhirCsModels4B.Subscription subscription)
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
            if (TryGetSubscription(subscriptionId, out fhirCsModels4B.Subscription subscription))
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

        /// <summary>Gets subscriptions bundle r 4.</summary>
        /// <param name="queryCollection">(Optional) The query collection.</param>
        /// <returns>The subscriptions bundle r 4.</returns>
        private fhirCsModels4B.Bundle _GetSubscriptionsBundle(IQueryCollection queryCollection = null)
        {
            fhirCsModels4B.Bundle bundle = new ()
            {
                Type = fhirCsModels4B.BundleTypeCodes.SEARCHSET,
                Total = (uint)_idSubscriptionDict.Count,
                Timestamp = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"),
                Meta = new ()
                {
                    LastUpdated = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"),
                },
                Entry = new (),
            };

            foreach (fhirCsModels4B.Subscription subscription in _idSubscriptionDict.Values)
            {
                string subscriptionUrl = Program.UrlForR4ResourceId("Subscription", subscription.Id);

                if ((queryCollection == null) || (queryCollection.Count == 0))
                {
                    bundle.Entry.Add(new ()
                    {
                        FullUrl = subscriptionUrl,
                        Resource = subscription,
                        Search = new ()
                        {
                            Mode = fhirCsModels4B.BundleEntrySearchModeCodes.MATCH,
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
                                        Mode = fhirCsModels4B.BundleEntrySearchModeCodes.MATCH,
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
                                        Mode = fhirCsModels4B.BundleEntrySearchModeCodes.MATCH,
                                    },
                                });
                            }

                            break;

                        case "url":
                            if (query.Value.Contains(subscription.Channel.Endpoint, StringComparer.OrdinalIgnoreCase))
                            {
                                bundle.Entry.Add(new ()
                                {
                                    FullUrl = subscriptionUrl,
                                    Resource = subscription,
                                    Search = new ()
                                    {
                                        Mode = fhirCsModels4B.BundleEntrySearchModeCodes.MATCH,
                                    },
                                });
                            }

                            break;

                        case "criteria":
                            if (query.Value.Contains(subscription.Criteria, StringComparer.OrdinalIgnoreCase) ||
                                query.Value.Contains(subscription.BackportTopicGet(), StringComparer.OrdinalIgnoreCase))
                            {
                                bundle.Entry.Add(new ()
                                {
                                    FullUrl = subscriptionUrl,
                                    Resource = subscription,
                                    Search = new ()
                                    {
                                        Mode = fhirCsModels4B.BundleEntrySearchModeCodes.MATCH,
                                    },
                                });
                            }
                            else
                            {
                                List<BackportedSubscriptionCs.FilterByComponent> filters = subscription.BackportFiltersGet();

                                foreach (BackportedSubscriptionCs.FilterByComponent filter in filters)
                                {
                                    if (query.Value.Contains(filter.ToString(), StringComparer.OrdinalIgnoreCase))
                                    {
                                        bundle.Entry.Add(new ()
                                        {
                                            FullUrl = subscriptionUrl,
                                            Resource = subscription,
                                            Search = new ()
                                            {
                                                Mode = fhirCsModels4B.BundleEntrySearchModeCodes.MATCH,
                                            },
                                        });
                                    }
                                }
                            }

                            break;
                    }
                }
            }

            bundle.Total = (uint)bundle.Entry.Count;

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

            List<string> keysToRemove = new ();

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
            List<fhirCsModels4B.Subscription> subscriptions = new ();

            fhirCsModels4B.Encounter csEncounter = null;
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
                    csEncounter = System.Text.Json.JsonSerializer.Deserialize<fhirCsModels4B.Encounter>(content);
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

                    // retrieve from the FHIR server
                    sdkEncounter = client.Read<Encounter>(location);

                    // convert to local object (needed for notifications)
                    csEncounter = JsonSerializer.Deserialize<fhirCsModels4B.Encounter>(
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
                List<string> searchKeys = new List<string>();

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
                foreach (fhirCsModels4B.Subscription subscription in subscriptions)
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
            ref List<fhirCsModels4B.Subscription> subscriptions)
        {
            // add subscriptions on this level
            if (node.SubscriptionsR4.Count > 0)
            {
                foreach (fhirCsModels4B.Subscription subscription in node.SubscriptionsR4)
                {
                    if (_idSubscriptionDict.ContainsKey(subscription.Id))
                    {
                        subscriptions.Add(_idSubscriptionDict[subscription.Id]);
                    }
                }
            }

            // check each of our keys
            foreach (string key in matchKeys)
            {
                // check for inclusions
                if (node.Inclusions.ContainsKey(key))
                {
                    FindSubscriptionsForKeys(node.Inclusions[key], matchKeys, matchHashSet, ref subscriptions);
                }
            }

            // check exclusions
            foreach (string key in node.Exclusions.Keys.ToArray<string>())
            {
                // check for not existing
                if (!matchHashSet.Contains(key))
                {
                    FindSubscriptionsForKeys(node.Exclusions[key], matchKeys, matchHashSet, ref subscriptions);
                }
            }
        }

        /// <summary>Handles a POST of a Subscription object.</summary>
        /// <exception cref="ArgumentException">Thrown when one or more arguments have unsupported or
        ///  illegal values.</exception>
        /// <param name="content">       The content.</param>
        /// <param name="subscription">  [out] The subscription.</param>
        /// <param name="statusCode">    [out] The status code.</param>
        /// <param name="failureContent">[out] The failure content.</param>
        private void _HandlePost(
            string content,
            out fhirCsModels4B.Subscription subscription,
            out HttpStatusCode statusCode,
            out string failureContent)
        {
            subscription = null;
            statusCode = System.Net.HttpStatusCode.Created;
            failureContent = string.Empty;

            // attempt to parse our content into a subscription request
            try
            {
                subscription = JsonSerializer.Deserialize<fhirCsModels4B.Subscription>(content);
                if (subscription == null)
                {
                    throw new ArgumentException("Invalid subscription content", nameof(content));
                }

                // check for invalid content type
                if (string.IsNullOrEmpty(subscription.Channel.Payload) ||
                    subscription.Channel.Payload.Contains("xml", StringComparison.OrdinalIgnoreCase))
                {
                    statusCode = HttpStatusCode.BadRequest;
                    failureContent = $"Invalid channel payload type: {subscription.Channel.Type}!" +
                        $" currently only 'application/fhir+json' is supported.";
                    return;
                }

                string topicUrl = subscription.BackportTopicGet();

                // check for the topic
                if (string.IsNullOrEmpty(topicUrl) ||
                    (!SubscriptionTopicManagerR4.IsImplemented(topicUrl)))
                {
                    statusCode = HttpStatusCode.BadRequest;
                    failureContent = $"Invalid SubscriptionTopic: {topicUrl}!";
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
                subscription.Status = fhirCsModels4B.SubscriptionStatusCodes.REQUESTED;

                _AddOrUpdate(subscription);

                bool shouldSendHandshake = false;

                if (subscription.BackportAdditionalChannelTypeTryGet(out string channelType))
                {
                    Console.WriteLine($" <<< extended channel: {channelType}");
                    switch (channelType)
                    {
                        case ZulipExtensions.ZulipChannelUrl:
                            shouldSendHandshake = true;
                            break;

                        //default:
                        //    statusCode = HttpStatusCode.BadRequest;
                        //    failureContent = $"Invalid channel type requested: {channelType}";
                        //    return;
                    }
                }
                else
                {
                    switch (subscription.Channel.Type)
                    {
                        case fhirCsModels4B.SubscriptionChannelTypeCodes.REST_HOOK:
                            if (string.IsNullOrEmpty(subscription.Channel.Endpoint) ||
                                (!Uri.TryCreate(subscription.Channel.Endpoint, UriKind.Absolute, out _)))
                            {
                                statusCode = HttpStatusCode.BadRequest;
                                failureContent = $"Invalid Endpoint for rest-hook subscription: {subscription.Channel.Endpoint}";
                                return;
                            }

                            // rest-hook sends handshake
                            shouldSendHandshake = true;
                            break;

                        case fhirCsModels4B.SubscriptionChannelTypeCodes.EMAIL:
                            // email sends handshake
                            shouldSendHandshake = true;
                            break;

                        case fhirCsModels4B.SubscriptionChannelTypeCodes.WEBSOCKET:
                            // websocket does NOT send handshake
                            shouldSendHandshake = false;
                            break;

                        default:
                            statusCode = HttpStatusCode.BadRequest;
                            failureContent = $"Invalid channel type requested: {subscription.Channel.Type}";
                            return;
                    }
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
                    _idSubscriptionDict[subscription.Id].Status = fhirCsModels4B.SubscriptionStatusCodes.ACTIVE;
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
        private bool _AddOrUpdate(fhirCsModels4B.Subscription subscription)
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

            if (subscription.BackportAdditionalChannelTypeTryGet(out string channelType))
            {
                Console.WriteLine($" <<< added Subscription:" +
                    $" {subscription.Id}" +
                    $" ({channelType})," +
                    $" {subscription.BackportTopicGet()}");
            }
            else
            {
                Console.WriteLine($" <<< added Subscription:" +
                    $" {subscription.Id}" +
                    $" ({subscription.Channel.Type})," +
                    $" {subscription.BackportTopicGet()}");
            }

            // get the topic for this subscription
            fhirCsModels4B.SubscriptionTopic topic = SubscriptionTopicManagerR4.GetTopic(subscription.BackportTopicGet());

            // check for unknown topic
            if (topic == null)
            {
                Console.WriteLine($"SubscriptionManager._AddOrUpdate <<< could not find topic: {subscription.BackportTopicGet()}");
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
                    _resourceSubscriptionDict.Add(resourceName, new SubscriptionFilterNode());
                }
            }
        }

        /// <summary>Track subscription.</summary>
        /// <param name="subscription">[out] The subscription.</param>
        /// <param name="topic">       The topic.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool TrackSubscription(
            fhirCsModels4B.Subscription subscription,
            fhirCsModels4B.SubscriptionTopic topic)
        {
            List<BackportedSubscriptionCs.FilterByComponent> filters = subscription.BackportFiltersGet();

            // check for unfiltered subscriptions
            if ((filters == null) || (!filters.Any()))
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
                    foreach (fhirCsModels4B.SubscriptionTopicResourceTrigger resourceTrigger in topic.ResourceTrigger)
                    {
                        if (string.IsNullOrEmpty(resourceTrigger.Resource))
                        {
                            continue;
                        }

                        TrackResource(resourceTrigger.Resource);
                        lock (_resourceSubscriptionDictLock)
                        {
                            _resourceSubscriptionDict[resourceTrigger.Resource].SubscriptionsR4.Add(subscription);
                        }
                    }
                }

                // done with this type of subscription
                return true;
            }

            // for now, reject any subscription that starts with an exclusion
            if (filters[0].Modifier == fhirCsModels5.SubscriptionFilterBySearchModifierCodes.NOT_IN)
            {
                Console.WriteLine($"SubscriptionManager.TrackSubscription <<< first match cannot be {filters[0].Modifier}");
                return false;
            }

            // sort by field, match, value
            filters.Sort(
                (a, b) => string.CompareOrdinal(
                    $"{a.FilterParameter}{a.Modifier}{a.Value}",
                    $"{b.FilterParameter}{b.Modifier}{b.Value}"));

            // loop over resources in the topic
            foreach (fhirCsModels4B.SubscriptionTopicResourceTrigger resourceTrigger in topic.ResourceTrigger)
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
            fhirCsModels4B.Subscription subscription,
            SubscriptionFilterNode node,
            List<BackportedSubscriptionCs.FilterByComponent> filters)
        {
            // check for no filters (done)
            if (filters.Count == 0)
            {
                // add the subscription to this node
                node.SubscriptionsR4.Add(subscription);

                // done
                return true;
            }

            // grab the first filter (sorted)
            BackportedSubscriptionCs.FilterByComponent filter = filters[0];
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
                string filterKey = $"{filter.FilterParameter}:{filterValue}";

                // add to the correct type
                switch (filter.Modifier)
                {
                    case fhirCsModels5.SubscriptionFilterBySearchModifierCodes.NOT_IN:
                    case fhirCsModels5.SubscriptionFilterBySearchModifierCodes.NE:
                        // make sure this field is in exclusions
                        lock (_resourceSubscriptionDictLock)
                        {
                            if (!node.Exclusions.ContainsKey(filterKey))
                            {
                                node.Exclusions.Add(filterKey, new SubscriptionFilterNode());
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
                                node.Inclusions.Add(filterKey, new SubscriptionFilterNode());
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
                            $" invalid MatchType: {filter.Modifier}! subscription: {subscription.Id}");
                        return false;
                }
            }

            return success;
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

            fhirCsModels4B.Subscription subscription = _idSubscriptionDict[subscriptionId];

            bool notified = TryNotifySubscription(subscriptionId, null, true);

            // attempt to send the notification
            if (notified)
            {
                // update in the manager (if it hasn't been removed)
                if (_idSubscriptionDict.ContainsKey(subscriptionId))
                {
                    _idSubscriptionDict[subscription.Id].Status = fhirCsModels4B.SubscriptionStatusCodes.ACTIVE;

                    // tell the user
                    Console.WriteLine($" <<< Subscription {subscription.Id} set to active!");
                }
            }
            else
            {
                // update to error in the manager (if it hasn't been removed)
                if (_idSubscriptionDict.ContainsKey(subscriptionId))
                {
                    _idSubscriptionDict[subscription.Id].Status = fhirCsModels4B.SubscriptionStatusCodes.ERROR;

                    // tell the user
                    Console.WriteLine($" <<< Subscription {subscription.Id} set to ERROR!");
                }
            }

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
                        Program.FhirServerUrlR4,
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
                    Console.WriteLine($"SubscriptionMananger.TryGetFhirClientR4 <<<" +
                        $" failed url: {Program.FhirServerUrlR4}," +
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
        /// Attempts to get subscription status r 4 the r4.Parameters from the given string.
        /// </summary>
        /// <param name="id">                  The identifier.</param>
        /// <param name="status">              [out] The status.</param>
        /// <param name="eventsInNotification">The events in notification.</param>
        /// <param name="isForQuery">          True if is for query, false if not.</param>
        /// <param name="isHandshake">         True if is handshake, false if not.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetSubscriptionStatus(
            string id,
            out fhirCsModels4B.SubscriptionStatus status,
            int eventsInNotification,
            bool isForQuery,
            bool isHandshake)
        {
            if (string.IsNullOrEmpty(id))
            {
                status = null;
                return false;
            }

            if (!TryGetSubscription(id, out fhirCsModels4B.Subscription subscription))
            {
                status = null;
                return false;
            }

            long eventCount = _instance._idEventCountDict[subscription.Id];

            status = new fhirCsModels4B.SubscriptionStatus()
            {
                EventsSinceSubscriptionStart = eventCount.ToString(),
                EventsInNotification = eventsInNotification,
                Status = subscription.Status.ToString().ToLowerInvariant(),
                Subscription = new fhirCsModels4B.Reference()
                {
                    ReferenceField = Program.UrlForR4ResourceId("Subscription", subscription.Id),
                },
                Topic = subscription.BackportTopicGet(),
            };

            if (isForQuery)
            {
                status.Type = fhirCsValueSets4B.SubscriptionNotificationTypeCodes.LiteralQueryStatus;
            }
            else if (eventsInNotification == 0)
            {
                if (isHandshake)
                {
                    status.Type = fhirCsValueSets4B.SubscriptionNotificationTypeCodes.LiteralHandshake;
                }
                else
                {
                    status.Type = fhirCsValueSets4B.SubscriptionNotificationTypeCodes.LiteralHeartbeat;
                }
            }
            else
            {
                status.Type = fhirCsValueSets4B.SubscriptionNotificationTypeCodes.LiteralEventNotification;
            }

            return true;
        }

        /// <summary>Bundle for subscription notification r 4.</summary>
        /// <exception cref="ArgumentNullException">Thrown when one or more required arguments are null.</exception>
        /// <param name="subscription">          The subscription.</param>
        /// <param name="content">               The content.</param>
        /// <param name="isHandshake">           True if is handshake, false if not.</param>
        /// <param name="bundle">                [out] The bundle.</param>
        /// <param name="subscriptionEventCount">[out] Number of events.</param>
        /// <param name="cachedNotification">    [out] The cached notification.</param>
        public static void BundleForSubscriptionNotification(
            fhirCsModels4B.Subscription subscription,
            fhirCsModels4B.Resource content,
            bool isHandshake,
            out fhirCsModels4B.Bundle bundle,
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
            bundle = new fhirCsModels4B.Bundle()
            {
                Type = fhirCsModels4B.BundleTypeCodes.HISTORY,
                Timestamp = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"),
                Entry = new List<fhirCsModels4B.BundleEntry>(),
            };

            if (TryGetSubscriptionStatus(
                    subscription.Id,
                    out fhirCsModels4B.SubscriptionStatus status,
                    (content == null) ? 0 : 1,
                    false,
                    isHandshake))
            {
                string url = Program.UrlForR4ResourceId("Subscription", subscription.Id) + "/$status";
                bundle.Entry.Add(new fhirCsModels4B.BundleEntry()
                {
                    FullUrl = url,
                    Request = new fhirCsModels4B.BundleEntryRequest()
                    {
                        Method = "GET",
                        Url = url,
                    },
                    Response = new fhirCsModels4B.BundleEntryResponse()
                    {
                        Status = "200",
                    },
                });
            }

            string subscriptionContent = subscription.BackportContentGet();

            if (content != null)
            {
                string url = Program.UrlForR4ResourceId(content.ResourceType, content.Id);

                switch (subscriptionContent)
                {
                    case BackportedSubscription.ContentCodeEmpty:
                        break;
                    case BackportedSubscription.ContentCodeIdOnly:
                        // add the URL, but no resource
                        bundle.Entry.Add(new fhirCsModels4B.BundleEntry()
                        {
                            FullUrl = url,
                            Request = new fhirCsModels4B.BundleEntryRequest()
                            {
                                Method = "POST",
                                Url = url,
                            },
                            Response = new fhirCsModels4B.BundleEntryResponse()
                            {
                                Status = "204",
                            },
                            //Extension = new List<fhirCsModels4B.Extension>()
                            //{
                            //    new fhirCsModels4B.Extension()
                            //    {
                            //        Url = BackportedSubscription.ExtensionUrlNotificationFocus,
                            //        ValueString = subscriptionEventCount.ToString(),
                            //    },
                            //},
                        });

                        status.NotificationEvent = new List<fhirCsModels4B.SubscriptionStatusNotificationEvent>()
                        {
                            new fhirCsModels4B.SubscriptionStatusNotificationEvent()
                            {
                                EventNumber = subscriptionEventCount.ToString(),
                                Focus = new fhirCsModels4B.Reference()
                                {
                                    ReferenceField = Program.UrlForR4ResourceId(content.ResourceType, content.Id),
                                },
                            },
                        };

                        break;
                    case BackportedSubscription.ContentCodeFullResource:
                        // add the URL and the resource
                        bundle.Entry.Add(new fhirCsModels4B.BundleEntry()
                        {
                            FullUrl = url,
                            Request = new fhirCsModels4B.BundleEntryRequest()
                            {
                                Method = "POST",
                                Url = url,
                            },
                            Response = new fhirCsModels4B.BundleEntryResponse()
                            {
                                Status = "204",
                            },
                            Resource = content,
                            //Extension = new List<fhirCsModels4B.Extension>()
                            //{
                            //    new fhirCsModels4B.Extension()
                            //    {
                            //        Url = BackportedSubscription.ExtensionUrlNotificationFocus,
                            //        ValueString = subscriptionEventCount.ToString(),
                            //    },
                            //},
                        });

                        status.NotificationEvent = new List<fhirCsModels4B.SubscriptionStatusNotificationEvent>()
                        {
                            new fhirCsModels4B.SubscriptionStatusNotificationEvent()
                            {
                                EventNumber = subscriptionEventCount.ToString(),
                                Focus = new fhirCsModels4B.Reference()
                                {
                                    ReferenceField = Program.UrlForR4ResourceId(content.ResourceType, content.Id),
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
                        (cachedNotification.AdditionalR4 != null) &&
                        cachedNotification.AdditionalR4.Any())
                    {
                        status.NotificationEvent[0].AdditionalContext = new List<fhirCsModels4B.Reference>();

                        foreach (string additional in cachedNotification.AdditionalR4.Keys)
                        {
                            status.NotificationEvent[0].AdditionalContext.Add(new fhirCsModels4B.Reference()
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
            fhirCsModels4B.Subscription subscription,
            fhirCsModels4B.Resource content,
            long eventNumber,
            ref fhirCsModels4B.Bundle bundle,
            out CachedNotificationEvent cachedNotification)
        {
            if (content == null)
            {
                cachedNotification = null;
                return true;
            }

            string subscriptionContent = subscription.BackportContentGet();

            FhirClient client = null;

            // get the topic
            string topicUrl = subscription.BackportTopicGet();

            if (string.IsNullOrEmpty(topicUrl))
            {
                cachedNotification = null;
                return false;
            }

            cachedNotification = new CachedNotificationEvent()
            {
                Focus = Program.UrlForR4ResourceId(content.ResourceType, content.Id),
                FocusR4 = content,
            };

            List<string> includeDirectives = new List<string>();

            SubscriptionTopicManagerR4.TryGetTopic(topicUrl, out fhirCsModels4B.SubscriptionTopic topic);

            if ((topic.NotificationShape == null) || (!topic.NotificationShape.Any()))
            {
                return true;
            }

            foreach (fhirCsModels4B.SubscriptionTopicNotificationShape shape in topic.NotificationShape)
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
                    fhirCsModels4B.Resource res = System.Text.Json.JsonSerializer.Deserialize<fhirCsModels4B.Resource>(json);

                    string fullUrl = Program.UrlForR4ResourceId(entry.Resource.TypeName, entry.Resource.Id);
                    cachedNotification.AdditionalR4.Add(fullUrl, res);

                    switch (subscriptionContent)
                    {
                        case BackportedSubscription.ContentCodeEmpty:
                            break;

                        case BackportedSubscription.ContentCodeIdOnly:
                            bundle.Entry.Add(new fhirCsModels4B.BundleEntry()
                            {
                                FullUrl = fullUrl,
                                Request = new fhirCsModels4B.BundleEntryRequest()
                                {
                                    Method = "GET",
                                    Url = fullUrl,
                                },
                                Response = new fhirCsModels4B.BundleEntryResponse()
                                {
                                    Status = "200",
                                },
                                //Extension = new List<fhirCsModels4B.Extension>()
                                //{
                                //    new fhirCsModels4B.Extension()
                                //    {
                                //        Url = BackportedSubscription.ExtensionUrlNotificationIncluded,
                                //        ValueString = eventNumber.ToString(),
                                //    },
                                //},
                            });

                            break;

                        case BackportedSubscription.ContentCodeFullResource:
                            bundle.Entry.Add(new fhirCsModels4B.BundleEntry()
                            {
                                FullUrl = fullUrl,
                                Request = new fhirCsModels4B.BundleEntryRequest()
                                {
                                    Method = "GET",
                                    Url = fullUrl,
                                },
                                Response = new fhirCsModels4B.BundleEntryResponse()
                                {
                                    Status = "200",
                                },
                                Resource = res,
                                //Extension = new List<fhirCsModels4B.Extension>()
                                //{
                                //    new fhirCsModels4B.Extension()
                                //    {
                                //        Url = BackportedSubscription.ExtensionUrlNotificationIncluded,
                                //        ValueString = eventNumber.ToString(),
                                //    },
                                //},
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
            out fhirCsModels4B.Bundle bundle)
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
            out fhirCsModels4B.Bundle bundle)
        {
            // sanity checks
            if (string.IsNullOrEmpty(subscriptionId) ||
                (!_idSubscriptionDict.ContainsKey(subscriptionId)))
            {
                bundle = null;
                return false;
            }

            fhirCsModels4B.Subscription subscription = _idSubscriptionDict[subscriptionId];

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

            fhirCsModels4B.SubscriptionStatus status = new ()
            {
                EventsSinceSubscriptionStart = _idEventCountDict[subscriptionId].ToString(),
                EventsInNotification = 0,
                Status = subscription.Status.ToString().ToLowerInvariant(),
                Type = fhirCsValueSets4B.SubscriptionNotificationTypeCodes.LiteralQueryEvent,
                Subscription = new ()
                {
                    ReferenceField = Program.UrlForR4ResourceId("Subscription", subscription.Id),
                },
                Topic = subscription.BackportTopicGet(),
                NotificationEvent = new (),
            };

            // create a bundle for this message message
            bundle = new ()
            {
                Type = fhirCsModels4B.BundleTypeCodes.HISTORY,
                Timestamp = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"),
                Entry = new (),
            };

            bundle.Entry.Add(new ()
            {
                FullUrl = Program.UrlForR4ResourceId("Subscription", subscription.Id) + "/$events",
                Request = new ()
                {
                    Method = "GET",
                    Url = Program.UrlForR4ResourceId("Subscription", subscription.Id) + "/$events",
                },
                Response = new ()
                {
                    Status = "200",
                },
                //Resource = status,
            });

            // if there's no content hint, use the subscription style
            if (string.IsNullOrEmpty(contentHint))
            {
                contentHint = subscription.BackportContentGet();

                // if we still don't have a content type, use id-only
                if (string.IsNullOrEmpty(contentHint))
                {
                    contentHint = BackportedSubscription.ContentCodeIdOnly;
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

                fhirCsModels4B.SubscriptionStatusNotificationEvent statusEvent = new ()
                {
                    EventNumber = i.ToString(),
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
                        //Extension = new List<fhirCsModels4B.Extension>()
                        //{
                        //    new fhirCsModels4B.Extension()
                        //    {
                        //        Url = BackportedSubscription.ExtensionUrlNotificationFocus,
                        //        ValueString = i.ToString(),
                        //    },
                        //},
                        Resource = (contentHint == BackportedSubscription.ContentCodeFullResource)
                            ? _subscriptionEventCache[subscriptionId][i].FocusR4
                            : null,
                    });
                }

                if (_subscriptionEventCache[subscriptionId][i].AdditionalR4.Count > 0)
                {
                    statusEvent.AdditionalContext = new ();
                }

                foreach (KeyValuePair<string, fhirCsModels4B.Resource> kvp in _subscriptionEventCache[subscriptionId][i].AdditionalR4)
                {
                    statusEvent.AdditionalContext.Add(new ()
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
                            //Extension = new List<fhirCsModels4B.Extension>()
                            //{
                            //    new fhirCsModels4B.Extension()
                            //    {
                            //        Url = BackportedSubscription.ExtensionUrlNotificationIncluded,
                            //        ValueString = i.ToString(),
                            //    },
                            //},
                            Resource = (contentHint == BackportedSubscription.ContentCodeFullResource)
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
                if (_idSubscriptionDict[subscriptionId].Status == fhirCsModels4B.SubscriptionStatusCodes.ERROR)
                {
                    // _idSubscriptionDict[subscription.Id].Error = null;
                    _idSubscriptionDict[subscriptionId].Status = fhirCsModels4B.SubscriptionStatusCodes.ACTIVE;
                }
            }
            else
            {
                // done
                // _idSubscriptionDict[subscription.Id].Error = ErrorConceptForString($"Endpoint returned: {response.ReasonPhrase}", (int)response.StatusCode);
                _idSubscriptionDict[subscriptionId].Status = fhirCsModels4B.SubscriptionStatusCodes.ERROR;
            }
        }

        /// <summary>Attempts to notify subscription a Resource from the given string.</summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="csContent">     (Optional) The fhirC# typed content.</param>
        /// <param name="isHandshake">   (Optional) True if is handshake, false if not.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool TryNotifySubscription(
            string subscriptionId,
            fhirCsModels4B.Resource csContent = null,
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

                string contentTypeName = (csContent == null) ? string.Empty : csContent.ResourceType;
                string contentId = (csContent == null) ? string.Empty : csContent.Id;

                // get the subscription
                fhirCsModels4B.Subscription subscription = _idSubscriptionDict[subscriptionId];

                // get our bundle we want to send
                BundleForSubscriptionNotification(
                    subscription,
                    csContent,
                    isHandshake,
                    out fhirCsModels4B.Bundle bundle,
                    out long subscriptionEventCount,
                    out CachedNotificationEvent cacheNotification);

                if (csContent == null)
                {
                    Console.WriteLine($"SubscriptionManagerR4.TryNotifySubscription <<<" +
                        $" attempting to send {subscriptionId}" +
                        $" heartbeat ({_idEventCountDict[subscriptionId]})" +
                        $" via {subscription.Channel.Type}...");
                }
                else
                {
                    Console.WriteLine($"SubscriptionManagerR4.TryNotifySubscription <<<" +
                        $" attempting to send {subscriptionId}" +
                        $" event {_idEventCountDict[subscriptionId]}" +
                        $" via {subscription.Channel.Type}...");
                }

                string json = JsonSerializer.Serialize(bundle);

                if (cacheNotification != null)
                {
                    _subscriptionEventCache[subscriptionId].Add(subscriptionEventCount, cacheNotification);
                    if (_subscriptionEventCache[subscriptionId].Count > MaxCachedEvents)
                    {
                        _subscriptionEventCache[subscriptionId].Remove(_subscriptionEventCache[subscriptionId].Keys.Min());
                    }
                }

                bool notified = false;

                // check for additional channel types
                if (subscription.BackportAdditionalChannelTypeTryGet(out string extendedChannelType))
                {
                    switch (extendedChannelType)
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
                                    Program.UrlForR4ResourceId("Subscription", subscriptionId),
                                    site,
                                    email,
                                    key,
                                    streamId,
                                    pmUserId,
                                    subscription.Channel.Payload,
                                    subscription.BackportContentGet(),
                                    json,
                                    contentTypeName,
                                    contentId,
                                    Program.UrlForR4ResourceId(contentTypeName, contentId),
                                    subscriptionEventCount);
                            }

                            UpdateErrorState(subscriptionId, notified);

                            // cannot have additional channel types
                            return notified;
                    }
                }

                switch (subscription.Channel.Type)
                {
                    case fhirCsModels4B.SubscriptionChannelTypeCodes.REST_HOOK:
                        // send via hook
                        notified = NotificationManager.TryNotifyRestHook(
                            subscriptionId,
                            subscription.Channel.Endpoint,
                            subscription.Channel.Header,
                            json,
                            contentTypeName,
                            contentId,
                            subscriptionEventCount);
                        break;

                    case fhirCsModels4B.SubscriptionChannelTypeCodes.EMAIL:
                        notified = NotificationManager.TryNotifyEmail(
                                                subscriptionId,
                                                subscription.Channel.Endpoint,
                                                subscription.Channel.Payload,
                                                subscription.BackportContentGet(),
                                                json,
                                                contentTypeName,
                                                contentId,
                                                subscriptionEventCount);
                        break;

                    case fhirCsModels4B.SubscriptionChannelTypeCodes.WEBSOCKET:
                        WebsocketManager.QueueMessagesForSubscription(subscription, json);

                        // assume anything via websockets works, since we don't have a good detection story
                        notified = true;
                        break;
                }

                UpdateErrorState(subscriptionId, notified);
                return notified;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubscriptionManagerR4.TryNotifySubscription <<< caught: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($" <<< inner: {ex.InnerException.Message}");
                }
            }

            // no notification has been sent
            return false;
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
                        fhirCsModels4B.Subscription subscription = _idSubscriptionDict[id];

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
                    fhirCsModels4B.Subscription subscription = _idSubscriptionDict[subscriptionId];

                    if (subscription.Status != fhirCsModels4B.SubscriptionStatusCodes.ACTIVE)
                    {
                        continue;
                    }

                    int heartbeatTimeout = subscription.BackportChannelHeartbeatGet();

                    if (heartbeatTimeout <= 0)
                    {
                        continue;
                    }

                    long thresholdTicks = currentTicks - TimeSpan.FromSeconds(heartbeatTimeout).Ticks;

                    // check timeout
                    if (_idLastEventTicksDict[subscriptionId] < thresholdTicks)
                    {
                        TryNotifySubscription(subscriptionId);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SubscriptionManagerR4.ProcessHeartbeats <<< caught: {ex.Message}");
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
                _instance = new SubscriptionManagerR4();
            }
        }
    }
}
