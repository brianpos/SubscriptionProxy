// <copyright file="SubscriptionTopicManagerR4.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

extern alias fhir5;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using fhirCsR4B.Models;
using fhirCsValueSets4B = fhirCsR4B.ValueSets;

namespace argonaut_subscription_server_proxy.Managers
{
    /// <summary>Manager for topics.</summary>
    public class SubscriptionTopicManagerR4
    {
        /// <summary>The instance for singleton pattern.</summary>
        private static SubscriptionTopicManagerR4 _instance;

        /// <summary>Dictionary of topic names to indicies in _topics.</summary>
        private Dictionary<string, SubscriptionTopic> _titleTopicDict;
        private Dictionary<string, SubscriptionTopic> _canonicalUrlTopicDict;
        private Dictionary<string, SubscriptionTopic> _localUrlTopicDict;
        private Dictionary<string, SubscriptionTopic> _idTopicDict;

        /// <summary>
        /// Prevents a default instance of the
        /// <see cref="SubscriptionTopicManagerR4"/> class from being
        /// created.
        /// </summary>
        private SubscriptionTopicManagerR4()
        {
            // create our index objects
            _titleTopicDict = new Dictionary<string, SubscriptionTopic>();
            _canonicalUrlTopicDict = new Dictionary<string, SubscriptionTopic>();
            _localUrlTopicDict = new Dictionary<string, SubscriptionTopic>();
            _idTopicDict = new Dictionary<string, SubscriptionTopic>();
        }

        /// <summary>Initializes this object.</summary>
        public static void Init()
        {
            // make an instance
            CheckOrCreateInstance();

            // setup our list of known topics
            _instance.CreateTopics();
        }

        /// <summary>Gets a list of all currently known topics.</summary>
        /// <returns>The topic list.</returns>
        public static List<SubscriptionTopic> GetTopicList()
        {
            // return our list of known topics
            return _instance._titleTopicDict.Values.ToList<SubscriptionTopic>();
        }

        /// <summary>Gets topic URL list.</summary>
        /// <returns>The topic URL list.</returns>
        public static List<string> GetTopicUrlList()
        {
            List<string> topics = new List<string>();

            foreach (SubscriptionTopic topic in _instance._titleTopicDict.Values)
            {
                topics.Add(topic.Url);
            }

            return topics;
        }

        /// <summary>Gets topics bundle.</summary>
        /// <param name="wrapInBasic">(Optional) True to wrap in basic.</param>
        /// <returns>The topics bundle.</returns>
        public static Bundle GetTopicsBundle(bool wrapInBasic = false)
        {
            return _instance._GetTopicsBundle(wrapInBasic);
        }

        /// <summary>Adds or updates a Topic</summary>
        /// <param name="topic">The topic.</param>
        public static void AddOrUpdate(SubscriptionTopic topic)
        {
            if (topic == null)
            {
                return;
            }

            _instance._AddOrUpdate(topic);
        }

        /// <summary>Query if 'canonical' is subscription topic implemented.</summary>
        /// <param name="url">The URL for the topic.</param>
        /// <returns>True if subscription topic implemented, false if not.</returns>
#pragma warning disable CA1054 // Uri parameters should not be strings
        public static bool IsImplemented(string url)
#pragma warning restore CA1054 // Uri parameters should not be strings
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            if (_instance._localUrlTopicDict.ContainsKey(url))
            {
                return true;
            }

            if (_instance._canonicalUrlTopicDict.ContainsKey(url))
            {
                return true;
            }

            return false;
        }

        /// <summary>Gets a topic.</summary>
        /// <param name="key">The key (Title, ID, URL)</param>
        /// <returns>The topic.</returns>
        public static SubscriptionTopic GetTopic(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_instance._titleTopicDict.ContainsKey(key))
            {
                return _instance._titleTopicDict[key];
            }

            if (_instance._idTopicDict.ContainsKey(key))
            {
                return _instance._idTopicDict[key];
            }

            if (_instance._localUrlTopicDict.ContainsKey(key))
            {
                return _instance._localUrlTopicDict[key];
            }

            if (_instance._canonicalUrlTopicDict.ContainsKey(key))
            {
                return _instance._canonicalUrlTopicDict[key];
            }

            // not found
            return null;
        }

        /// <summary>Attempts to get topic a SubscriptionTopic from the given string.</summary>
        /// <param name="key">  The key for the topic</param>
        /// <param name="topic">[out] The topic.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetTopic(string key, out SubscriptionTopic topic)
        {
            topic = GetTopic(key);

            return topic != null;
        }

        /// <summary>Attempts to get serialized a string from the given string.</summary>
        /// <param name="key">       The key for the topic</param>
        /// <param name="serialized">[out] The serialized.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool TryGetSerialized(string key, out string serialized)
        {
            if (TryGetTopic(key, out SubscriptionTopic resource))
            {
                serialized = JsonSerializer.Serialize(resource);

                return true;
            }

            serialized = null;
            return false;
        }

        /// <summary>Gets topics bundle.</summary>
        /// <param name="wrapInBasic">(Optional) True to wrap in basic.</param>
        /// <returns>The topics bundle.</returns>
        private Bundle _GetTopicsBundle(bool wrapInBasic = false)
        {
            Bundle bundle = new Bundle()
            {
                Type = BundleTypeCodes.SEARCHSET,
                Total = (uint)_titleTopicDict.Count,
                Meta = new Meta()
                {
                    LastUpdated = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"),
                },
                Entry = new List<BundleEntry>(),
            };

            foreach (SubscriptionTopic topic in _idTopicDict.Values)
            {
                bundle.Entry.Add(new BundleEntry()
                {
                    FullUrl = Program.UrlForR4ResourceId("SubscriptionTopic", topic.Id),
                    Resource = (Resource)topic,
                    Search = new BundleEntrySearch
                    {
                        Mode = BundleEntrySearchModeCodes.MATCH,
                    },
                });
            }

            // return our bundle
            return bundle;
        }

        /// <summary>Adds or updates a Topic</summary>
        /// <param name="topic">The topic.</param>
        private void _AddOrUpdate(SubscriptionTopic topic)
        {
            string localUrl = Program.UrlForR4ResourceId("SubscriptionTopic", topic.Id);

            // check for local url already existing
            if (_localUrlTopicDict.ContainsKey(localUrl))
            {
                SubscriptionTopic oldTopic = _localUrlTopicDict[localUrl];

                // remove if this topic exists in other dictionaries
                RemoveIfExists(_canonicalUrlTopicDict, oldTopic.Url);
                RemoveIfExists(_titleTopicDict, localUrl);
                RemoveIfExists(_idTopicDict, oldTopic.Title);

                // remove from this dict
                _localUrlTopicDict.Remove(topic.Title);
            }

            // check for this canonical url already existing
            if (_canonicalUrlTopicDict.ContainsKey(topic.Url))
            {
                SubscriptionTopic oldTopic = _canonicalUrlTopicDict[topic.Url];

                // remove if this topic exists in other dictionaries
                RemoveIfExists(_localUrlTopicDict, localUrl);
                RemoveIfExists(_titleTopicDict, oldTopic.Url);
                RemoveIfExists(_idTopicDict, oldTopic.Title);

                // remove from this dict
                _canonicalUrlTopicDict.Remove(topic.Url);
            }

            // check for title already existing
            if (_titleTopicDict.ContainsKey(topic.Title))
            {
                SubscriptionTopic oldTopic = _titleTopicDict[topic.Title];

                // remove if this topic exists in other dictionaries
                RemoveIfExists(_localUrlTopicDict, localUrl);
                RemoveIfExists(_canonicalUrlTopicDict, oldTopic.Url);
                RemoveIfExists(_idTopicDict, oldTopic.Title);

                // remove from this dict
                _titleTopicDict.Remove(topic.Title);
            }

            // check for this id already existing
            if (_idTopicDict.ContainsKey(topic.Id))
            {
                SubscriptionTopic oldTopic = _idTopicDict[topic.Id];

                // remove if this topic exists in other dictionaries
                RemoveIfExists(_localUrlTopicDict, localUrl);
                RemoveIfExists(_canonicalUrlTopicDict, oldTopic.Url);
                RemoveIfExists(_titleTopicDict, oldTopic.Title);

                // remove from this dict
                _idTopicDict.Remove(topic.Id);
            }

            // add to local url dictionary
            _localUrlTopicDict.Add(localUrl, topic);

            // add to canonical url dictionary
            _canonicalUrlTopicDict.Add(topic.Url, topic);

            // add to the title dictionary
            _titleTopicDict.Add(topic.Title, topic);

            // add to id dictionary
            _idTopicDict.Add(topic.Id, topic);
        }

        /// <summary>Removes if exists.</summary>
        /// <param name="dict">The dictionary.</param>
        /// <param name="key"> The key (Title, ID, URL).</param>
        private static void RemoveIfExists(Dictionary<string, SubscriptionTopic> dict, string key)
        {
            if (dict.ContainsKey(key))
            {
                dict.Remove(key);
            }
        }

        /// <summary>Creates the topics.</summary>
        private void CreateTopics()
        {
            // make sure our lists are clear
            _titleTopicDict.Clear();
            _canonicalUrlTopicDict.Clear();
            _idTopicDict.Clear();

            // create our known topics
            SubscriptionTopic topic = new SubscriptionTopic()
            {
                Title = "encounter-start",
                Id = "encounter-start",
                Url = "http://argonautproject.org/encounters-ig/SubscriptionTopic/encounter-start",
                Version = "1.1",
                Status = SubscriptionTopicStatusCodes.DRAFT,
                Experimental = true,
                Description = "Beginning of a clinical encounter",
                Date = "2021-08-03",
                ResourceTrigger = new List<SubscriptionTopicResourceTrigger>()
                {
                    new SubscriptionTopicResourceTrigger()
                    {
                        Description = "Beginning of a clinical encounter",
                        Resource = "Encounter",
                        QueryCriteria = new SubscriptionTopicResourceTriggerQueryCriteria()
                        {
                            Previous = "status:not=in-progress",
                            ResultForCreate = SubscriptionTopicResourceTriggerQueryCriteriaResultForCreateCodes.TEST_PASSES,
                            Current = "status:in-progress",
                            ResultForDelete = SubscriptionTopicResourceTriggerQueryCriteriaResultForDeleteCodes.TEST_FAILS,
                            RequireBoth = true,
                        },
                        FhirPathCriteria = "%previous.status!='in-progress' and %current.status='in-progress'",
                    },
                },
                CanFilterBy = new List<SubscriptionTopicCanFilterBy>()
                {
                    new SubscriptionTopicCanFilterBy()
                    {
                        Description = "Exact match to a patient resource (reference)",
                        Resource = "Encounter",
                        FilterParameter = "patient",
                        // FilterParameter = "http://hl7.org/fhir/build/SearchParameter/Encounter-patient",
                        Modifier = new List<string>()
                        {
                            fhirCsValueSets4B.SubscriptionSearchModifierCodes.LiteralEqual,
                            fhirCsValueSets4B.SubscriptionSearchModifierCodes.LiteralIn,
                        },
                    },
                },
                NotificationShape = new List<SubscriptionTopicNotificationShape>()
                {
                    new SubscriptionTopicNotificationShape()
                    {
                        Resource = "Encounter",
                        Include = new List<string>()
                        {
                            "Encounter:patient",
                            "Encounter:practitioner",
                            "Encounter:observation",
                            "Encounter:location",
                        },
                    },
                },
            };

            // add this topic
            _AddOrUpdate(topic);
        }

        /// <summary>Check or create instance.</summary>
        private static void CheckOrCreateInstance()
        {
            if (_instance == null)
            {
                _instance = new SubscriptionTopicManagerR4();
            }
        }
    }
}
