using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CoolStore.ProductCatalogApi.Apis.Gateways;
using CoolStore.ProductCatalogApi.Apis.GraphQL;
using CoolStore.ProductCatalogApi.Domain;
using CoolStore.ProductCatalogApi.Infrastructure.Persistence;
using CoolStore.Protobuf.Inventory.V1;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Playground;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using N8T.Infrastructure;
using N8T.Infrastructure.Dapr;
using N8T.Infrastructure.Data;
using N8T.Infrastructure.GraphQL;
using N8T.Infrastructure.Grpc;
using N8T.Infrastructure.Tye;
using N8T.Infrastructure.ValidationModel;
using OpenTelemetry.Trace;
using OpenTelemetry.Trace.Samplers;
using N8T.Infrastructure.OTel;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var (builder, config) = WebApplication.CreateBuilder(args)
    .AddCustomConfiguration();

var appOptions = config.GetOptions<AppOptions>("app");
Console.WriteLine(Figgle.FiggleFonts.Doom.Render($"{appOptions.Name}"));

_ = builder.Services
    .AddHttpContextAccessor()
    .AddCustomMediatR(typeof(Product))
    .AddCustomValidators(typeof(Product))
    .AddCustomDbContext<ProductCatalogDbContext>(
        typeof(Product),
        config.GetConnectionString(Consts.SQLSERVER_DB_ID))
    .AddCustomGraphQL(c =>
    {
        c.RegisterQueryType<QueryType>();
        c.RegisterMutationType<MutationType>();
        c.RegisterObjectTypes(typeof(Product));
        c.RegisterExtendedScalarTypes();
    })
    .AddCustomGrpcClient(svc =>
    {
        svc.AddGrpcClient<InventoryApi.InventoryApiClient>(o =>
        {
            o.Address = config.GetGrpcUriFor(Consts.INVENTORY_API_ID);
        });
    })
    .AddCustomDaprClient()
    .AddScoped<IInventoryGateway, InventoryGateway>()
    .AddOpenTelemetry(b => b
        .SetSampler(new AlwaysOnSampler())
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddSqlClientDependencyInstrumentation()
        .AddMediatRInstrumentation()
        .UseZipkinExporter(o =>
        {
            o.ServiceName = "product-catalog-api";
            o.Endpoint = new Uri($"http://{config.GetServiceUri("zipkin")?.DnsSafeHost}:9411/api/v2/spans");
        })
    );

var app = builder.Build();

app.UseStaticFiles()
    .UseGraphQL("/graphql")
    .UsePlayground(new PlaygroundOptions {QueryPath = "/graphql", Path = "/playground"})
    .UseRouting()
    .UseCloudEvents()
    .UseEndpoints(endpoints =>
    {
        endpoints.MapGet("/", context =>
        {
            context.Response.Redirect("/playground");
            return Task.CompletedTask;
        });
        endpoints.MapSubscribeHandler();
    });

await app.RunAsync();
