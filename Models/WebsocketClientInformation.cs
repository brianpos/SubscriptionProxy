// <copyright file="WebsocketClientInformation.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace argonaut_subscription_server_proxy.Models
{
    /// <summary>Information about the websocket client.</summary>
    public class WebsocketClientInformation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebsocketClientInformation"/> class.
        /// </summary>
        public WebsocketClientInformation()
        {
            MessageQ = new ConcurrentQueue<string>();
            SubscriptionIdSet = new HashSet<string>();
            BoundTokenGuids = new HashSet<Guid>();
        }

        /// <summary>Gets or sets the UID.</summary>
        /// <value>The UID.</value>
        public Guid Uid { get; set; }

        /// <summary>Gets or sets the type of the payload.</summary>
        /// <value>The type of the payload.</value>
        public string PayloadType { get; set; }

        /// <summary>Gets or sets the message q.</summary>
        /// <value>The message q.</value>
        public ConcurrentQueue<string> MessageQ { get; set; }

        /// <summary>Gets or sets the set the subscription identifiers belongs to.</summary>
        /// <value>The subscription identifiers set.</value>
        public HashSet<string> SubscriptionIdSet { get; set; }

        /// <summary>Gets or sets the bound token guids.</summary>
        public HashSet<Guid> BoundTokenGuids { get; set; }

        /// <summary>Client payload types.</summary>
        public sealed class WebsocketPayloadTypes
        {
            /// <summary>The fourth r.</summary>
            public const string R4 = "r4";

            /// <summary>The empty.</summary>
            public const string Empty = "empty";

            /// <summary>The identifier only.</summary>
            public const string IdOnly = "id-only";

            /// <summary>The full resource.</summary>
            public const string FullResource = "full-resource";
        }
    }
}
