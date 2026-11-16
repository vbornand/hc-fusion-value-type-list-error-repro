using System.Text;
using HotChocolate.Types.Composite;

// Two source schemas share the type Item, which has no lookup: Fusion composes it as a value type.
// Source schema A returns a list of items under a page object, and every item breaks its non-null
// field `name`, so A nulls the list and reports the errors. The gateway returns the null list
// without any error.

const string Query = "{ itemPage { items { name } } }";

var sourceSchemaA = CreateSourceSchema("A", 5201, b => b.AddQueryType<QueryA>(d => d.Name("Query")));
var sourceSchemaB = CreateSourceSchema("B", 5202, b => b.AddQueryType<QueryB>(d => d.Name("Query")));

if (args is ["export"])
{
    // Writes the source schema files that `nitro fusion compose` turns into gateway.far.
    foreach (var (name, app) in new[] { ("A", sourceSchemaA), ("B", sourceSchemaB) })
    {
        var schema = await app.Services.GetRequiredService<HotChocolate.Execution.IRequestExecutorProvider>()
            .GetExecutorAsync(name);
        await File.WriteAllTextAsync(System.IO.Path.Combine(name, "schema.graphqls"), schema.Schema.ToString());
    }

    Console.WriteLine("Exported A/schema.graphqls and B/schema.graphqls.");
    return;
}

var gatewayBuilder = WebApplication.CreateBuilder();
gatewayBuilder.WebHost.UseUrls("http://localhost:5200");
gatewayBuilder.Logging.ClearProviders();
gatewayBuilder.Services.AddHttpClient("A", c => c.BaseAddress = new Uri("http://localhost:5201/graphql"));
gatewayBuilder.Services.AddHttpClient("B", c => c.BaseAddress = new Uri("http://localhost:5202/graphql"));
gatewayBuilder.Services
    .AddGraphQLGatewayServer()
    .AddFileSystemConfiguration(System.IO.Path.Combine(AppContext.BaseDirectory, "gateway.far"))
    // The lists carry no @listSize, which would make the cost analysis refuse the query.
    .ModifyCostOptions(o => o.EnforceCostLimits = false);
var gateway = gatewayBuilder.Build();
gateway.MapGraphQL();

await sourceSchemaA.StartAsync();
await sourceSchemaB.StartAsync();
await gateway.StartAsync();

using var http = new HttpClient();
Console.WriteLine($"Query: {Query}");
Console.WriteLine();
Console.WriteLine("Source schema A (http://localhost:5201/graphql):");
Console.WriteLine(await PostAsync("http://localhost:5201/graphql"));
Console.WriteLine();
Console.WriteLine("Gateway (http://localhost:5200/graphql):");
Console.WriteLine(await PostAsync("http://localhost:5200/graphql"));

await gateway.StopAsync();
await sourceSchemaB.StopAsync();
await sourceSchemaA.StopAsync();

async Task<string> PostAsync(string url)
{
    var body = new StringContent($$"""{"query":"{{Query}}"}""", Encoding.UTF8, "application/json");
    var response = await http.PostAsync(url, body);
    return await response.Content.ReadAsStringAsync();
}

static WebApplication CreateSourceSchema(
    string name,
    int port,
    Action<HotChocolate.Execution.Configuration.IRequestExecutorBuilder> configure)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls($"http://localhost:{port}");
    builder.Logging.ClearProviders();
    var graphql = builder.Services.AddGraphQLServer(name);
    configure(graphql);
    var app = builder.Build();
    app.MapGraphQL(schemaName: name);
    return app;
}

public sealed class QueryA
{
    public ItemPage GetItemPage() => new();
}

// Not shared: only source schema A has it.
public sealed class ItemPage
{
    public List<Item>? GetItems() => [new Item(), new Item()];
}

// Shared by A and B, without a lookup: a value type in the composite schema.
[Shareable]
public sealed class Item
{
    // Non-null in the schema, null at runtime: HC0018 on every item.
    public string Name => null!;
}

public sealed class QueryB
{
    public Item? GetItem() => null;
}
