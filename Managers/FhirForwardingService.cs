// <copyright file="FhirForwardingService.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using argonaut_subscription_server_proxy.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace argonaut_subscription_server_proxy.Managers
{
    /// <summary>A service for accessing FHIR forwardings information.</summary>
    public class FhirForwardingService : IHostedService, IDisposable
    {
        /// <summary>The instance.</summary>
        private static FhirForwardingService _instance = null;

        /// <summary>The FHIR R4 Client.</summary>
        private HttpClient _clientR4;

        /// <summary>The FHIR R5 Client.</summary>
        private HttpClient _clientR5;

        /// <summary>The FHIR R4 URI.</summary>
        private Uri _uriR4;

        /// <summary>The FHIR R5 URI.</summary>
        private Uri _uriR5;

        /// <summary>True if we have been disposed.</summary>
        private bool _disposedValue;

        /// <summary>The ignored headers.</summary>
        private static readonly HashSet<string> _ignoredHeaders;

        /// <summary>
        /// Initializes static members of the <see cref="FhirForwardingService"/> class.
        /// </summary>
        static FhirForwardingService()
        {
            _ignoredHeaders = new HashSet<string>()
            {
                "Content-Type",
                "Accept",
                "Content-Length",
                "Host",
                "Accept-Encoding",
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirForwardingService"/> class.
        /// </summary>
        public FhirForwardingService()
        {
            _instance = this;
        }

        /// <summary>Gets the current forwarding service.</summary>
        public static FhirForwardingService Current => _instance;

        /// <summary>Triggered when the application host is ready to start the service.</summary>
        /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
        /// <returns>An asynchronous result.</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            string urlR4 = Program.Configuration["Server_FHIR_Url_R4"];
            string urlR5 = Program.Configuration["Server_FHIR_Url_R5"];

            if (string.IsNullOrEmpty(urlR4))
            {
                _clientR4 = null;
            }
            else
            {
                _uriR4 = new Uri(urlR4);
                _clientR4 = new HttpClient();

                Console.WriteLine($"FhirForwardingService <<< created client for R4: {urlR4}");
            }

            if (string.IsNullOrEmpty(urlR5))
            {
                _clientR5 = null;
            }
            else
            {
                _uriR5 = new Uri(urlR5);
                _clientR5 = new HttpClient();

                Console.WriteLine($"FhirForwardingService <<< created client for R5: {urlR5}");
            }

            return Task.CompletedTask;
        }

        /// <summary>Method from string.</summary>
        /// <exception cref="BadHttpRequestException">Thrown when a Bad HTTP Request error condition occurs.</exception>
        /// <param name="method">The method.</param>
        /// <returns>A HttpMethod.</returns>
        public static HttpMethod MethodFromString(string method)
        {
            switch (method.ToUpperInvariant())
            {
                case "DELETE":
                    return HttpMethod.Delete;
                case "GET":
                    return HttpMethod.Get;
                case "HEAD":
                    return HttpMethod.Head;
                case "POST":
                    return HttpMethod.Post;
                case "PUT":
                    return HttpMethod.Put;
                case "TRACE":
                    return HttpMethod.Trace;
            }

            throw new BadHttpRequestException($"Invalid HTTP Method: {method}");
        }

        /// <summary>Sends a response.</summary>
        /// <param name="context"> The context.</param>
        /// <param name="response">The response.</param>
        /// <returns>An asynchronous result.</returns>
        public async Task SendResponse(HttpContext context, ForwarderResponse response)
        {
            if (string.IsNullOrEmpty(response.ContentType))
            {
                context.Response.ContentType = "application/fhir+json";
            }
            else
            {
                context.Response.ContentType = response.ContentType;
            }

            if (!string.IsNullOrEmpty(response.Location))
            {
                context.Response.Headers.Add("Location", response.Location);
            }

            context.Response.StatusCode = (int)response.StatusCode;

            await context.Response.WriteAsync(response.Body);
        }

        /// <summary>Adds a FHIR headers to 'fhirRequest'.</summary>
        /// <param name="incomingRequest">The incoming request.</param>
        /// <param name="fhirRequest">    The FHIR request.</param>
        private void AddFhirHeaders(
            HttpRequest incomingRequest,
            HttpRequestMessage fhirRequest)
        {
            if (incomingRequest.Headers.Any())
            {
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> reqHeader in incomingRequest.Headers)
                {
                    if (_ignoredHeaders.Contains(reqHeader.Key))
                    {
                        continue;
                    }

                    fhirRequest.Headers.Add(reqHeader.Key, reqHeader.Value.ToArray());
                }
            }

            fhirRequest.Headers.Add("Accept", "application/fhir+json");
        }

        /// <summary>Adds a content to 'fhirRequest'.</summary>
        /// <param name="incomingRequest">The incoming request.</param>
        /// <param name="fhirRequest">    The FHIR request.</param>
        /// <param name="response">       [in,out] The response.</param>
        private void AddContent(
            HttpRequest incomingRequest,
            HttpRequestMessage fhirRequest,
            ref ForwarderResponse response)
        {
            response.InitialBody = string.Empty;

            if (incomingRequest.Body != null)
            {
                using (TextReader reader = new StreamReader(incomingRequest.Body))
                {
                    response.InitialBody = reader.ReadToEndAsync().Result;
                }

                if (!string.IsNullOrEmpty(response.InitialBody))
                {
                    fhirRequest.Content = new StringContent(response.InitialBody, Encoding.UTF8, "application/fhir+json");
                }
            }
        }

        /// <summary>Forward a request to a FHIR R4 Server.</summary>
        /// <param name="incomingRequest">The incoming request.</param>
        /// <returns>An asynchronous result that yields a ForwarderResponse.</returns>
        public async Task<ForwarderResponse> RequestR4(
            HttpRequest incomingRequest)
        {
            ForwarderResponse forwarderResponse = new ForwarderResponse()
            {
                Method = MethodFromString(incomingRequest.Method),
            };

            HttpRequestMessage fhirRequest = null;

            try
            {
                string relativePath = incomingRequest.Path;

                if (relativePath[0] == '/')
                {
                    relativePath = relativePath.Substring(1);
                }

                if (incomingRequest.QueryString.HasValue)
                {
                    relativePath += incomingRequest.QueryString.ToString();
                }

                fhirRequest = new HttpRequestMessage(
                    forwarderResponse.Method,
                    new Uri(_uriR4, relativePath));

                AddFhirHeaders(incomingRequest, fhirRequest);
                AddContent(incomingRequest, fhirRequest, ref forwarderResponse);

                HttpResponseMessage fhirResponse = await _clientR4.SendAsync(fhirRequest);

                forwarderResponse.StatusCode = fhirResponse.StatusCode;
                forwarderResponse.IsSuccessStatusCode = fhirResponse.IsSuccessStatusCode;

                forwarderResponse.Body = await fhirResponse.Content.ReadAsStringAsync();

                if (fhirResponse.Headers.Contains("Location"))
                {
                    forwarderResponse.Location = fhirResponse.Headers.Location.ToString();
                }

                //if (fhirResponse.Headers.Contains("Content-Type"))
                //{
                //    forwarderResponse.ContentType = fhirResponse.Headers.GetValues("Content-Type").First();
                //}
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FhirForwardingService.RequestR4 <<<" +
                    $" {incomingRequest.Method} request:" +
                    $" {incomingRequest.Path}?{incomingRequest.QueryString}" +
                    $" caused exception: {ex.Message}");
            }
            finally
            {
                if (fhirRequest != null)
                {
                    fhirRequest.Dispose();
                    fhirRequest = null;
                }
            }

            return forwarderResponse;
        }

        /// <summary>Forward a request to a FHIR R5 Server.</summary>
        /// <param name="incomingRequest">The incoming request.</param>
        /// <returns>An asynchronous result that yields a ForwarderResponse.</returns>
        public async Task<ForwarderResponse> RequestR5(
            HttpRequest incomingRequest)
        {
            ForwarderResponse forwarderResponse = new ForwarderResponse()
            {
                Method = MethodFromString(incomingRequest.Method),
            };

            HttpRequestMessage fhirRequest = null;

            try
            {
                string relativePath = incomingRequest.Path;

                if (relativePath[0] == '/')
                {
                    relativePath = relativePath.Substring(1);
                }

                if (incomingRequest.QueryString.HasValue)
                {
                    relativePath += incomingRequest.QueryString.ToString();
                }

                fhirRequest = new HttpRequestMessage(
                    forwarderResponse.Method,
                    new Uri(_uriR5, relativePath));

                AddFhirHeaders(incomingRequest, fhirRequest);
                AddContent(incomingRequest, fhirRequest, ref forwarderResponse);

                HttpResponseMessage fhirResponse = await _clientR5.SendAsync(fhirRequest);

                forwarderResponse.StatusCode = fhirResponse.StatusCode;
                forwarderResponse.IsSuccessStatusCode = fhirResponse.IsSuccessStatusCode;
                forwarderResponse.Body = await fhirResponse.Content.ReadAsStringAsync();

                if (fhirResponse.Headers.Contains("Location"))
                {
                    forwarderResponse.Location = fhirResponse.Headers.Location.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FhirForwardingService.RequestR5 <<<" +
                    $" {incomingRequest.Method} request:" +
                    $" {incomingRequest.Path}?{incomingRequest.QueryString}" +
                    $" caused exception: {ex.Message}");
            }
            finally
            {
                if (fhirRequest != null)
                {
                    fhirRequest.Dispose();
                    fhirRequest = null;
                }
            }

            return forwarderResponse;
        }

        /// <summary>Triggered when the application host is performing a graceful shutdown.</summary>
        /// <param name="cancellationToken">Indicates that the shutdown process should no longer be
        ///  graceful.</param>
        /// <returns>An asynchronous result.</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Releases the unmanaged resources used by the
        /// argonaut_subscription_server_proxy.Services.WebsocketHeartbeatService and optionally releases
        /// the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to
        ///  release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _clientR4.Dispose();
                    _clientR4 = null;

                    _clientR5.Dispose();
                    _clientR5 = null;
                }

                _disposedValue = true;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged
        /// resources.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
