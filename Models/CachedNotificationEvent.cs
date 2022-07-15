// <copyright file="CachedNotificationEvent.cs" company="Microsoft Corporation">
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
    /// <summary>A cached notification event.</summary>
    public class CachedNotificationEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CachedNotificationEvent"/> class.
        /// </summary>
        public CachedNotificationEvent()
        {
            AdditionalR4 = new Dictionary<string, Hl7.Fhir.Model.Resource>();
        }

        /// <summary>Gets or sets the focus.</summary>
        public string Focus { get; set; }

        /// <summary>Gets or sets the focus resource for FHIR R4.</summary>
        public Hl7.Fhir.Model.Resource FocusR4 { get; set; }

        /// <summary>Gets or sets the additional resources for FHIR R4.</summary>
        public Dictionary<string, Hl7.Fhir.Model.Resource> AdditionalR4 { get; set; }
    }
}
