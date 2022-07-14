using Hl7.Fhir.DemoFileSystemFhirServer;
using Hl7.Fhir.NetCoreApi;
using Hl7.Fhir.WebApi;


// Workaround for the R4B Citation resource
if (!Hl7.Fhir.Model.ModelInfo.FhirTypeToCsType.ContainsKey("Citation"))
{
    Hl7.Fhir.Model.ModelInfo.FhirTypeToCsType.Add("Citation", typeof(Hl7.Fhir.Model.Citation));
    Hl7.Fhir.Model.ModelInfo.FhirCsTypeToString.Add(typeof(Hl7.Fhir.Model.Citation), "Citation");
}

var builder = WebApplication.CreateBuilder(args);

DirectorySystemService<System.IServiceProvider>.Directory = @"c:\temp\subs-proxy";
builder.Services.AddSingleton<IFhirSystemServiceR4<IServiceProvider>>((s) => {
    var systemService = new DirectorySystemService<System.IServiceProvider>();
    systemService.InitializeIndexes();
    return systemService;
});

builder.Services.UseFhirServerController(options =>
{
    options.OutputFormatters.Add(new SimpleHtmlFhirOutputFormatter());
});

// CORS Support
builder.Services.AddCors(o => o.AddDefaultPolicy(builder =>
{
    // Better to use with Origins to only permit locations that we really trust
    builder.AllowAnyOrigin();
    // builder.WithOrigins(settings.AllowedOrigins);
    builder.AllowAnyHeader();
    builder.AllowAnyMethod();
    // builder.AllowCredentials();
    builder.WithExposedHeaders(new[] { "Content-Location", "Location", "Etag" });
}));
var app = builder.Build();

app.UseCors();
app.UseRouting();
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    // endpoints.MapFhirSmartAppLaunchController(); // this requires additional registration, but more later...
});

// ensure that the fhirpath engine is all set to go
Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();

app.Run();
