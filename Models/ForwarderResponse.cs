// <copyright file="ProxyResponse.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace argonaut_subscription_server_proxy.Models
{
    /// <summary>A proxy response.</summary>
    public class ForwarderResponse
    {
        /// <summary>Gets or sets the method.</summary>
        public HttpMethod Method { get; set; }

        /// <summary>Gets or sets the status code.</summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this object is success status code.
        /// </summary>
        public bool IsSuccessStatusCode { get; set; }

        /// <summary>Gets or sets the body.</summary>
        public string Body { get; set; }

        /// <summary>Gets or sets the initial body.</summary>
        public string InitialBody { get; set; }

        /// <summary>Gets or sets the location.</summary>
        public string Location { get; set; }

        /// <summary>Gets or sets the type of the content.</summary>
        public string ContentType { get; set; }
    }
}
