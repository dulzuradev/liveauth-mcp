using System.Net;
using System.Text;
using LiveAuthCore.Services.PermitSignal;
using Xunit;

namespace LiveAuthCore.Tests.PermitSignal;

public sealed class PermitAdapterTests
{
    [Fact]
    public async Task Austin_adapter_maps_official_schema_and_provenance()
    {
        const string json = """
        [{"project_id":"13745594","permit_number":"2026-097432 PP","permit_type_desc":"Plumbing Permit","work_class":"Commercial Remodel","description":"Install water line","status_current":"Active","applieddate":"2026-04-06T00:00:00.000","issue_date":"2026-08-05T00:00:00.000","statusdate":"2026-08-06T00:00:00.000","total_job_valuation":"250000","original_address1":"500 CONGRESS AVE","permit_class_mapped":"Commercial","latitude":"30.2","longitude":"-97.7","contractor_company_name":"Test Plumbing","link":{"url":"https://abc.austintexas.gov/permit/13745594"}}]
        """;
        var handler = new StubHandler(json);
        var page = await new AustinPermitAdapter(new HttpClient(handler))
            .FetchAsync(new PermitFetchRequest(new DateTime(2026, 8, 1), 0, 100), default);
        var record = Assert.Single(page.Records);
        Assert.Equal("13745594", record.SourceRecordId);
        Assert.Equal("Commercial", record.ResidentialOrCommercial);
        Assert.Equal(250000m, record.EstimatedProjectValue);
        Assert.Equal("https", handler.LastUri!.Scheme);
        Assert.Equal("data.austintexas.gov", handler.LastUri.Host);
        Assert.Contains("statusdate", handler.LastUri.Query);
    }

    [Fact]
    public async Task SanFrancisco_adapter_builds_address_and_uses_revised_value()
    {
        const string json = """
        [{"record_id":"1545854157209","permit_number":"201903226060","permit_type_definition":"additions alterations or repairs","street_number":"760","street_name":"14th","street_suffix":"St","unit":"2","description":"commercial tenant renovation","status":"issued","filed_date":"2026-08-01T00:00:00.000","issued_date":"2026-08-05T00:00:00.000","revised_cost":"97000.0","estimated_cost":"15000.0","proposed_use":"office","location":{"type":"Point","coordinates":[-122.43,37.76]},"data_loaded_at":"2026-08-06T04:38:02.190"}]
        """;
        var page = await new SanFranciscoPermitAdapter(new HttpClient(new StubHandler(json)))
            .FetchAsync(new PermitFetchRequest(new DateTime(2026, 8, 1), 0, 100), default);
        var record = Assert.Single(page.Records);
        Assert.Equal("760 14th St UNIT 2", record.Address);
        Assert.Equal(97000m, record.EstimatedProjectValue);
        Assert.Equal("Commercial", record.ResidentialOrCommercial);
        Assert.Equal(37.76m, record.Latitude);
    }

    [Fact]
    public async Task Seattle_adapter_maps_explicit_occupancy_and_project_cost()
    {
        const string json = """
        [{"permitnum":"6068103-CN","permitclassmapped":"Non-Residential","permittypemapped":"Building","permittypedesc":"Addition/Alteration","description":"Commercial tenant improvement","estprojectcost":"700000","applieddate":"2026-07-01T00:00:00.000","issueddate":"2026-08-05T00:00:00.000","statuscurrent":"Issued","originaladdress1":"1200 2ND AVE","contractorcompanyname":"Cascadia Builders","link":{"url":"https://services.seattle.gov/permit/6068103-CN"},"latitude":"47.60","longitude":"-122.33"}]
        """;
        var page = await new SeattlePermitAdapter(new HttpClient(new StubHandler(json)))
            .FetchAsync(new PermitFetchRequest(new DateTime(2026, 8, 1), 0, 100), default);
        var record = Assert.Single(page.Records);
        Assert.Equal("Commercial", record.ResidentialOrCommercial);
        Assert.Equal(700000m, record.EstimatedProjectValue);
        Assert.Equal("Seattle", record.Municipality);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
