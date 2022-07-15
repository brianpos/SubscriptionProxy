using Hl7.Fhir.DemoFileSystemFhirServer;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.WebApi;

namespace SubscriptionProxy.Models
{
    public class SystemService : Hl7.Fhir.WebApi.IFhirSystemServiceR4<IServiceProvider>
    {
        public SystemService()
        {
            var _dirSource = new DirectorySource(Directory, new DirectorySourceSettings() { ExcludeSummariesForUnknownArtifacts = true, MultiThreaded = true, Mask = "*..xml" });
            var cacheResolver = new CachedResolver(
                new MultiResolver(
                    _dirSource,
                    ZipSource.CreateValidationSource()
                    ));
            _source = cacheResolver;
        }

        /// <summary>
        /// The File system directory that will be scanned for the storage of FHIR resources
        /// </summary>
        public static string Directory { get; set; }
        private SearchIndexer _indexer;
        public int DefaultPageSize { get; set; } = 40;

        private CachedResolver _source;

        public void InitializeIndexes()
        {
            _indexer = new SearchIndexer();
            _indexer.Initialize(Directory);
        }

        public async Task<CapabilityStatement> GetConformance(ModelBaseInputs<IServiceProvider> request, SummaryType summary)
        {
            Hl7.Fhir.Model.CapabilityStatement con = new Hl7.Fhir.Model.CapabilityStatement();
            con.Url = request.BaseUri + "metadata";
            con.Description = new Markdown("Demonstration Directory based FHIR Subscriptions Proxy server");
            con.DateElement = new Hl7.Fhir.Model.FhirDateTime("2022-07-15");
            con.Version = "1.0.0.0";
            con.Name = "demoSubsProxy";
            con.Experimental = true;
            con.Status = PublicationStatus.Active;
            con.FhirVersion = FHIRVersion.N4_3_0;
            // con.AcceptUnknown = CapabilityStatement.UnknownContentCode.Extensions;
            con.Format = new string[] { "xml", "json" };
            con.Kind = CapabilityStatementKind.Instance;
            con.Meta = new Meta();
            con.Meta.LastUpdatedElement = Instant.Now();

            con.Rest = new List<Hl7.Fhir.Model.CapabilityStatement.RestComponent>
            {
                new Hl7.Fhir.Model.CapabilityStatement.RestComponent()
                {
                    Operation = new List<Hl7.Fhir.Model.CapabilityStatement.OperationComponent>()
                }
            };
            con.Rest[0].Mode = CapabilityStatement.RestfulCapabilityMode.Server;
            con.Rest[0].Resource = new List<Hl7.Fhir.Model.CapabilityStatement.ResourceComponent>();

            foreach (var resName in ModelInfo.SupportedResources)
            {
                var c = await GetResourceService(request, resName).GetRestResourceComponent();
                if (c != null)
                    con.Rest[0].Resource.Add(c);
            }

            return con;
        }

        public IFhirResourceServiceR4<IServiceProvider> GetResourceService(ModelBaseInputs<IServiceProvider> request, string resourceName)
        {
            if (!Hl7.Fhir.Model.ModelInfo.IsCoreModelType(resourceName))
                throw new NotImplementedException();

            return new ResourceProxy(request, resourceName, Directory, _source, _source) { Indexer = _indexer };
        }

        public Task<Resource> PerformOperation(ModelBaseInputs<IServiceProvider> request, string operation, Parameters operationParameters, SummaryType summary)
        {
            if (operation == "convert")
            {
                Resource resource = operationParameters.GetResource("input");
                if (resource != null)
                    return Task<Resource>.FromResult(resource);
                OperationOutcome outcome = new OperationOutcome();
                return Task<Resource>.FromResult(outcome as Resource);
            }
            throw new NotImplementedException();
        }

        public async Task<Bundle> ProcessBatch(ModelBaseInputs<IServiceProvider> request, Bundle batch)
        {
            BatchOperationProcessing<IServiceProvider> batchProcessor = new BatchOperationProcessing<IServiceProvider>();
            batchProcessor.DefaultPageSize = DefaultPageSize;
            batchProcessor.GetResourceService = GetResourceService;
            return await batchProcessor.ProcessBatch(request, batch);
        }

        public Task<Bundle> Search(ModelBaseInputs<IServiceProvider> request, IEnumerable<KeyValuePair<string, string>> parameters, int? Count, SummaryType summary)
        {
            throw new NotImplementedException();
        }

        public Task<Bundle> SystemHistory(ModelBaseInputs<IServiceProvider> request, DateTimeOffset? since, DateTimeOffset? Till, int? Count, SummaryType summary)
        {
            throw new NotImplementedException();
        }
    }
}
