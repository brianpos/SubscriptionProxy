using Hl7.Fhir.DemoFileSystemFhirServer;
using Hl7.Fhir.Language.Debugging;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.WebApi;

namespace SubscriptionProxy.Models
{
    public class ResourceProxy : DirectoryResourceService<IServiceProvider>, Hl7.Fhir.WebApi.IFhirResourceServiceR4<IServiceProvider>
    {
        public ResourceProxy(ModelBaseInputs<IServiceProvider> requestDetails, string resourceName, string directory, IResourceResolver Source, IAsyncResourceResolver AsyncSource)
            : base(requestDetails, resourceName, directory, Source, AsyncSource)
        {

        }

        new public async Task<Resource> Create(Resource resource, string ifMatch, string ifNoneExist, DateTimeOffset? ifModifiedSince)
        {
            var mode = ResourceValidationMode.create;
            Resource? oldResource = null;
            if (!String.IsNullOrEmpty(resource.Id))
            {
                try
                {
                    oldResource = await base.Get(resource.Id, null, SummaryType.False);
                    mode = ResourceValidationMode.update;
                }
                catch (FhirServerException ex)
                {
                    if (ex.StatusCode != System.Net.HttpStatusCode.NotFound && ex.StatusCode != System.Net.HttpStatusCode.Gone)
                        throw ex;
                }
            }

            // Validate the resource before storing (optional)
            var outcome = await base.ValidateResource(resource, mode, null);
            if (!outcome.Success)
                throw new FhirServerException(System.Net.HttpStatusCode.BadRequest, outcome);

            var newResource = await base.Create(resource, ifMatch, ifNoneExist, ifModifiedSince);
            await SubmitSubscriptionProcessingRequest(oldResource, newResource, RequestDetails.HttpMethod);
            return newResource;
        }

        new public async Task<string> Delete(string id, string ifMatch)
        {
            Resource? oldResource = null;
            try
            {
                oldResource = await base.Get(id, null, SummaryType.False);
            }
            catch (FhirServerException ex)
            {
                if (ex.StatusCode != System.Net.HttpStatusCode.NotFound && ex.StatusCode != System.Net.HttpStatusCode.Gone)
                    throw ex;
            }
            var result = await base.Delete(id, ifMatch);
            // Queue up this change
            await SubmitSubscriptionProcessingRequest(oldResource, null, RequestDetails.HttpMethod);
            return result;
        }

        public System.Threading.Tasks.Task SubmitSubscriptionProcessingRequest(Resource? oldVersion, Resource? newVersion, string method)
        {
            System.Diagnostics.Trace.WriteLine($"Old Resource: {oldVersion?.ResourceIdentity().OriginalString ?? "(null)"}");
            System.Diagnostics.Trace.WriteLine($"New Resource: {newVersion?.ResourceIdentity().OriginalString ?? "(null)"}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        new public Task<Resource> PerformOperation(string id, string operation, Parameters operationParameters, SummaryType summary)
        {
            return base.PerformOperation(id, operation, operationParameters, summary);
        }

        new public Task<Resource> PerformOperation(string operation, Parameters operationParameters, SummaryType summary)
        {
            return base.PerformOperation(operation, operationParameters, summary);
        }
    }
}
