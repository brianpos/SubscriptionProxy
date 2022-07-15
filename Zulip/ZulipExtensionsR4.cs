// <copyright file="ZulipExtensionsR4.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using r4s = Hl7.Fhir.Serialization;

namespace argonaut_subscription_server_proxy.Zulip
{
    /// <summary>A zulip extensions r 4.</summary>
    public static class ZulipExtensionsR4
    {
        /// <summary>The extension URL site.</summary>
        public const string ExtensionUrlSite = "http://fhir-extension.zulip.org/site";

        /// <summary>The extension URL email.</summary>
        public const string ExtensionUrlEmail = "http://fhir-extension.zulip.org/email";

        /// <summary>The extension URL key.</summary>
        public const string ExtensionUrlKey = "http://fhir-extension.zulip.org/key";

        /// <summary>Identifier for the extension URL stream.</summary>
        public const string ExtensionUrlStreamId = "http://fhir-extension.zulip.org/stream-id";

        /// <summary>Identifier for the extension URL pm user.</summary>
        public const string ExtensionUrlPmUserId = "http://fhir-extension.zulip.org/pm-user-id";

        /// <summary>URL of the zulip channel.</summary>
        public const string ZulipChannelUrl = "http://fhir-extensions.zulip.org/subscription-channel-type#zulip";

        /// <summary>
        /// A r4.Subscription extension method that backport zulip pm user identifier try get.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool R4ZulipPmUserIdTryGet(this Subscription resource, out string value)
        {
            value = resource?.GetStringExtension(ExtensionUrlPmUserId);
            if (!string.IsNullOrEmpty(value))
                return true;
            return false;
        }

        /// <summary>
        /// A r4.Subscription extension method that backport zulip stream identifier try get.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool R4ZulipStreamIdTryGet(this Subscription resource, out string value)
        {
            value = resource?.GetStringExtension(ExtensionUrlStreamId);
            if (!string.IsNullOrEmpty(value))
                return true;
            return false;
        }

        /// <summary>A r4.Subscription extension method that backport zulip key try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool R4ZulipKeyTryGet(this Subscription resource, out string value)
        {
            value = resource?.GetStringExtension(ExtensionUrlKey);
            if (!string.IsNullOrEmpty(value))
                return true;
            return false;
        }

        /// <summary>A r4.Subscription extension method that backport zulip email try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool R4ZulipEmailTryGet(this Subscription resource, out string value)
        {
            value = resource?.GetStringExtension(ExtensionUrlEmail);
            if (!string.IsNullOrEmpty(value))
                return true;
            return false;
        }

        /// <summary>A r4.Subscription extension method that backport zulip site try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool R4ZulipSiteTryGet(this Subscription resource, out string value)
        {
            value = resource?.GetStringExtension(ExtensionUrlSite);
            if (!string.IsNullOrEmpty(value))
                return true;
            return false;
        }
    }
}
