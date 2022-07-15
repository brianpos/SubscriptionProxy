using Hl7.Fhir.DemoFileSystemFhirServer;
using Hl7.Fhir.Language.Debugging;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Utility;
using Hl7.Fhir.WebApi;
using Hl7.FhirPath.Sprache;
using System.Collections.Generic;

namespace SubscriptionProxy.Models
{
    public class ResourceProxy : IFhirResourceServiceR4<IServiceProvider>
    {
        /// <summary>
        /// Queue up this change to be processed by a subscription processor
        /// </summary>
        /// <param name="oldVersion"></param>
        /// <param name="newVersion"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        public System.Threading.Tasks.Task SubmitSubscriptionProcessingRequest(Resource? oldVersion, Resource? newVersion, string method)
        {
            System.Diagnostics.Trace.WriteLine($"Old Resource: {oldVersion?.ResourceIdentity().OriginalString ?? "(null)"}");
            System.Diagnostics.Trace.WriteLine($"New Resource: {newVersion?.ResourceIdentity().OriginalString ?? "(null)"}");
            return System.Threading.Tasks.Task.CompletedTask;
        }


        DirectoryResourceService<IServiceProvider> _local;
        private string _proxyToServer;

        public string ResourceName => _local.ResourceName;

        public ModelBaseInputs<IServiceProvider> RequestDetails => _local.RequestDetails;

        public ResourceProxy(string proxyToServer, ModelBaseInputs<IServiceProvider> requestDetails, string resourceName, string directory, IResourceResolver Source, IAsyncResourceResolver AsyncSource, SearchIndexer indexer)
        {
            _local = new DirectoryResourceService<IServiceProvider>(requestDetails, resourceName, directory, Source, AsyncSource) { Indexer = indexer };
            _proxyToServer = proxyToServer;
        }

        public async Task<Resource> Create(Resource resource, string ifMatch, string ifNoneExist, DateTimeOffset? ifModifiedSince)
        {
            var mode = DirectoryResourceService<IServiceProvider>.ResourceValidationMode.create;
            Resource? oldResource = null;
            if (!String.IsNullOrEmpty(resource.Id))
            {
                try
                {
                    oldResource = await Get(resource.Id, null, SummaryType.False);
                    mode = DirectoryResourceService<IServiceProvider>.ResourceValidationMode.update;
                }
                catch (FhirServerException ex)
                {
                    if (ex.StatusCode != System.Net.HttpStatusCode.NotFound && ex.StatusCode != System.Net.HttpStatusCode.Gone)
                        throw ex;
                }
            }

            // Validate the resource before storing (optional)
            var outcome = await _local.ValidateResource(resource, mode, null);
            if (!outcome.Success)
                throw new FhirServerException(System.Net.HttpStatusCode.BadRequest, outcome);

            Resource newResource;
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
            {
                newResource = await _local.Create(resource, ifMatch, ifNoneExist, ifModifiedSince);
            }
            else
            {
                // hit the proxy'd server to create the instance
                FhirClient client = new FhirClient(_proxyToServer);
                client.PreferredFormat = ResourceFormat.Json;
                try
                {
                    if (mode == DirectoryResourceService<IServiceProvider>.ResourceValidationMode.create)
                    {
                        newResource = await client.CreateAsync(resource);
                        newResource.SetAnnotation(CreateOrUpate.Create);
                    }
                    else
                    {
                        newResource = await client.UpdateAsync(resource);
                        newResource.SetAnnotation(CreateOrUpate.Update);
                    }
                    newResource.ResourceBase = RequestDetails.BaseUri;
                }
                catch (FhirOperationException fex)
                {
                    throw new FhirServerException(fex.Status, fex.Outcome);
                }
            }
            await SubmitSubscriptionProcessingRequest(oldResource, newResource, RequestDetails.HttpMethod);
            return newResource;
        }

        public async Task<string> Delete(string id, string ifMatch)
        {
            Resource? oldResource = null;
            try
            {
                oldResource = await Get(id, null, SummaryType.False);
            }
            catch (FhirServerException ex)
            {
                if (ex.StatusCode != System.Net.HttpStatusCode.NotFound && ex.StatusCode != System.Net.HttpStatusCode.Gone)
                    throw ex;
            }
            string result = null;
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
            {
                result = await _local.Delete(id, ifMatch);
            }
            else
            {
                // hit the proxy'd server to create the instance
                FhirClient client = new FhirClient(_proxyToServer);
                await client.DeleteAsync($"{ResourceName}/{id}");
            }
            // Queue up this change
            await SubmitSubscriptionProcessingRequest(oldResource, null, RequestDetails.HttpMethod);
            return result;
        }

        public async Task<Resource> PerformOperation(string id, string operation, Parameters operationParameters, SummaryType summary)
        {
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
                return await _local.PerformOperation(id, operation, operationParameters, summary);

            FhirClient client = new FhirClient(_proxyToServer);
            client.PreferredFormat = ResourceFormat.Json;
            Uri url = new Uri($"{_proxyToServer}{ResourceName}/{id}", UriKind.RelativeOrAbsolute);
            try
            {
                var result = await client.InstanceOperationAsync(url, operation, operationParameters);
                result.ResourceBase = RequestDetails.BaseUri;
                return result;
            }
            catch (FhirOperationException fex)
            {
                throw new FhirServerException(fex.Status, fex.Outcome);
            }
        }

        public async Task<Resource> PerformOperation(string operation, Parameters operationParameters, SummaryType summary)
        {
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
                return await _local.PerformOperation(operation, operationParameters, summary);

            FhirClient client = new FhirClient(_proxyToServer);
            client.PreferredFormat = ResourceFormat.Json;
            try
            {
                var result = await client.TypeOperationAsync(operation, ResourceName, operationParameters);
                result.ResourceBase = RequestDetails.BaseUri;
                return result;
            }
            catch (FhirOperationException fex)
            {
                throw new FhirServerException(fex.Status, fex.Outcome);
            }
        }

        public async Task<Resource> Get(string resourceId, string VersionId, SummaryType summary)
        {
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
                return await _local.Get(resourceId, VersionId, summary);

            FhirClient client = new FhirClient(_proxyToServer);
            string url = $"{ResourceName}/{resourceId}";
            if (!string.IsNullOrEmpty(VersionId))
                url = $"{ResourceName}/{resourceId}/_history/{VersionId}";
            if (!string.IsNullOrEmpty(RequestDetails.RequestUri.Query))
                url += "?" + RequestDetails.RequestUri.Query;
            try
            {
                var result = await client.GetAsync(url);
                result.ResourceBase = RequestDetails.BaseUri;
                return result;
            }
            catch (FhirOperationException fex)
            {
                throw new FhirServerException(fex.Status, fex.Outcome);
            }
        }

        public async Task<Bundle> TypeHistory(DateTimeOffset? since, DateTimeOffset? till, int? count, SummaryType summary)
        {
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
                return await _local.TypeHistory(since, till, count, summary);

            FhirClient client = new FhirClient(_proxyToServer);
            try
            {
                var result = await client.TypeHistoryAsync(ResourceName, since, count, summary);
                result.ResourceBase = RequestDetails.BaseUri;
                return result;
            }
            catch (FhirOperationException fex)
            {
                throw new FhirServerException(fex.Status, fex.Outcome);
            }
        }

        async Task<Bundle> IFhirResourceServiceR4<IServiceProvider>.InstanceHistory(string ResourceId, DateTimeOffset? since, DateTimeOffset? till, int? count, SummaryType summary)
        {
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
                return await _local.InstanceHistory(ResourceId, since, till, count, summary);

            FhirClient client = new FhirClient(_proxyToServer);
            try
            {
                var result = await client.HistoryAsync($"{ResourceName}/{ResourceId}", since, count, summary);
                result.ResourceBase = RequestDetails.BaseUri;
                return result;
            }
            catch (FhirOperationException fex)
            {
                throw new FhirServerException(fex.Status, fex.Outcome);
            }
        }

        async Task<Bundle> IFhirResourceServiceR4<IServiceProvider>.Search(IEnumerable<KeyValuePair<string, string>> parameters, int? Count, SummaryType summary, string sortby)
        {
            if (ResourceName == "SubscriptionTopic" || string.IsNullOrEmpty(_proxyToServer))
                return await _local.Search(parameters, Count, summary, sortby);

            FhirClient client = new FhirClient(_proxyToServer);
            string url = $"{ResourceName}";
            if (!string.IsNullOrEmpty(RequestDetails.RequestUri.Query))
                url += "?" + RequestDetails.RequestUri.Query;
            try
            {
                var result = await client.GetAsync(url);
                result.ResourceBase = RequestDetails.BaseUri;
                return result as Bundle;
            }
            catch (FhirOperationException fex)
            {
                throw new FhirServerException(fex.Status, fex.Outcome);
            }
        }

        public Task<CapabilityStatement.ResourceComponent> GetRestResourceComponent()
        {
            return _local.GetRestResourceComponent();
        }
    }
}
