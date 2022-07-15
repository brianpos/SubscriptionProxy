// <copyright file="ZulipExtensions.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Linq;
using fhirCsModels4B = fhirCsR4B.Models;
using fhirCsModels5 = fhirCsR5.Models;

namespace argonaut_subscription_server_proxy.Zulip
{
    /// <summary>A zulip extensions r 4.</summary>
    public static class ZulipExtensions
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
        /// A fhirCsModels4B.Subscription extension method that backport zulip pm user identifier try get.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipPmUserIdTryGet(this fhirCsModels4B.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels4B.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlPmUserId)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// A fhirCsModels5.Subscription extension method that backport zulip pm user identifier try get.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipPmUserIdTryGet(this fhirCsModels5.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels5.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlPmUserId)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// A fhirCsModels4B.Subscription extension method that backport zulip stream identifier try get.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipStreamIdTryGet(this fhirCsModels4B.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels4B.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlStreamId)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// A fhirCsModels5.Subscription extension method that backport zulip stream identifier try get.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipStreamIdTryGet(this fhirCsModels5.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels5.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlStreamId)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>A fhirCsModels4B.Subscription extension method that backport zulip key try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipKeyTryGet(this fhirCsModels4B.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels4B.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlKey)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>A fhirCsModels4B.Subscription extension method that backport zulip key try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipKeyTryGet(this fhirCsModels5.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels5.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlKey)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>A fhirCsModels4B.Subscription extension method that backport zulip email try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipEmailTryGet(this fhirCsModels4B.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels4B.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlEmail)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>A fhirCsModels5.Subscription extension method that backport zulip email try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipEmailTryGet(this fhirCsModels5.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels5.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlEmail)
                {
                    value = ext.ValueString;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>A fhirCsModels4B.Subscription extension method that backport zulip site try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipSiteTryGet(this fhirCsModels4B.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels4B.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlSite)
                {
                    if (!string.IsNullOrEmpty(ext.ValueString))
                    {
                        value = ext.ValueString;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(ext.ValueUri))
                    {
                        value = ext.ValueUri;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(ext.ValueUrl))
                    {
                        value = ext.ValueUrl;
                        return true;
                    }

                    value = null;
                    return false;
                }
            }

            value = null;
            return false;
        }

        /// <summary>A fhirCsModels5.Subscription extension method that backport zulip site try get.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="value">   [out] The value.</param>
        /// <returns>True if it succeeds, false if it fails.</returns>
        public static bool ZulipSiteTryGet(this fhirCsModels5.Subscription resource, out string value)
        {
            if ((resource == null) ||
                (resource.Extension == null) ||
                (!resource.Extension.Any()))
            {
                value = null;
                return false;
            }

            foreach (fhirCsModels5.Extension ext in resource.Extension)
            {
                if (ext.Url == ExtensionUrlSite)
                {
                    if (!string.IsNullOrEmpty(ext.ValueString))
                    {
                        value = ext.ValueString;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(ext.ValueUri))
                    {
                        value = ext.ValueUri;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(ext.ValueUrl))
                    {
                        value = ext.ValueUrl;
                        return true;
                    }

                    value = null;
                    return false;
                }
            }

            value = null;
            return false;
        }
    }
}
