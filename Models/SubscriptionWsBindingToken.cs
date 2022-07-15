// <copyright file="SubscriptionWsBindingToken.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace argonaut_subscription_server_proxy.Models
{
    /// <summary>A subscription ws binding token.</summary>
    public class SubscriptionWsBindingToken
    {
        /// <summary>The bound client.</summary>
        private Guid _boundClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionWsBindingToken"/> class.
        /// </summary>
        /// <param name="subscriptionIds">A list of identifiers of the subscriptions.</param>
        /// <param name="fhirVersion">    The FHIR version.</param>
        public SubscriptionWsBindingToken(
            List<string> subscriptionIds,
            int fhirVersion)
        {
            Token = Guid.NewGuid();
            AddedAt = DateTime.Now;
            ExpiresAt = DateTime.Now.AddMinutes(30);
            SubscriptionIds = subscriptionIds;
            FhirVersion = fhirVersion;
            _boundClient = Guid.Empty;
        }

        /// <summary>Gets the token.</summary>
        public Guid Token { get; }

        /// <summary>Gets the Date/Time of the added at.</summary>
        public DateTime AddedAt { get; }

        /// <summary>Gets the Date/Time of the expires at.</summary>
        public DateTime ExpiresAt { get; }

        /// <summary>Gets a list of identifiers of the subscriptions.</summary>
        public List<string> SubscriptionIds { get; }

        /// <summary>Gets the bound client.</summary>
        public Guid BoundClient => _boundClient;

        /// <summary>Gets the FHIR version.</summary>
        public int FhirVersion { get; }

        /// <summary>Gets token R4.</summary>
        /// <param name="subscriptionIds">A list of identifiers of the subscriptions.</param>
        /// <returns>The token r 4.</returns>
        public static SubscriptionWsBindingToken GetTokenR4(List<string> subscriptionIds)
        {
            return new SubscriptionWsBindingToken(subscriptionIds, 4);
        }

        /// <summary>Gets token R5.</summary>
        /// <param name="subscriptionIds">A list of identifiers of the subscriptions.</param>
        /// <returns>The token r 5.</returns>
        public static SubscriptionWsBindingToken GetTokenR5(List<string> subscriptionIds)
        {
            return new SubscriptionWsBindingToken(subscriptionIds, 5);
        }

        /// <summary>Bind to client.</summary>
        /// <param name="clientToken">The client token.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public bool BindToClient(Guid clientToken)
        {
            if (_boundClient != Guid.Empty)
            {
                return false;
            }

            _boundClient = clientToken;
            return true;
        }
    }
}
