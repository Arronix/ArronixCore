using Arronix.Abstractions.Events;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Telemetry;
using Arronix.Api.Configuration;
using Arronix.Api.Endpoints;
using Arronix.Api.Hosting;
using Arronix.Api.Hubs;
using Arronix.Api.OpenApi;
using Arronix.Api.Security;
using Arronix.Api.Serialization;
using Arronix.Host.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;


// The composition root of the Arronix server.
//
// It reads as a table of contents on purpose. Every subsystem is registered by an explicit call to a named
// extension method, in an order that can be checked by eye, with no assembly scanning anywhere — a host
// that cannot enumerate what it registered cannot withhold anything from an extension either, and the
// whole capability model rests on being able to say exactly what is in the container.
var builder = WebApplication.CreateBuilder(args);

// --- where this server is installed ---------------------------------------------------------------
// Nothing if no installation root is configured. When one is, the packages folder, the per-package state
// folder, the database file and the client's static root are all derived from it, so an installation is
// one fact rather than four settings that have to be kept in agreement by whoever launched the process.
builder.Configuration.AddArronixInstallation(builder.Environment.ContentRootPath);

// --- the platform ---------------------------------------------------------------------------------
// One call brings up configuration, the clock, the filesystem, the extension loader and its isolation,
// the media-kind and provider registries, storage, the scheduler and health aggregation. Extensions are
// activated by a hosted service after the container is built, so they never mutate it and never run before
// configuration and logging exist.
builder.Services.AddArronixHost(builder.Configuration);

// --- this server's own settings -------------------------------------------------------------------
builder.Services
    .AddOptionsWithValidateOnStart<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .ValidateDataAnnotations();

// --- how everything is written --------------------------------------------------------------------
// The REST surface and the event stream share one description of the wire format. They have to: the same
// contract types travel over both, and a client that could deserialize a payload from one but not the
// other would be a bug nobody finds until something is already broken in front of a user.
builder.Services.ConfigureHttpJsonOptions(static options => ApiJsonOptions.Apply(options.SerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(static options => ApiJsonOptions.Apply(options.PayloadSerializerOptions));

// A refusal is a problem document, everywhere, including the ones the framework produces on its own.
builder.Services.AddProblemDetails();

// --- the live connection --------------------------------------------------------------------------
// The broadcaster is a translator, not a second message bus: it subscribes to the domain events the host
// already raises and to the telemetry it already emits, and turns them into envelopes. Registering it
// under each of those contracts rather than giving it a publish method of its own is what stops a future
// subsystem from having to remember to report the same fact twice.
builder.Services.AddSingleton<EventBroadcaster>();
builder.Services.AddSingleton<IEventHandler<IDomainEvent>>(static services => services.GetRequiredService<EventBroadcaster>());
builder.Services.AddSingleton<IEventHandler<ProviderDefinitionChanged>>(static services => services.GetRequiredService<EventBroadcaster>());
builder.Services.AddSingleton<ITelemetrySink>(static services => services.GetRequiredService<EventBroadcaster>());
builder.Services.AddHostedService<HealthChangeWatcher>();

// Endpoint filters are resolved from the container, so the one that withholds declared secrets has to be
// registerable. It is a singleton because it holds nothing but the registry it reads declarations from.
builder.Services.TryAddSingleton<SecretRedactionFilter>();

// --- cross-origin ---------------------------------------------------------------------------------
// Empty by default, and that is the point: a client served by this host needs nothing here, so anything in
// this list is a deliberate decision to host the client somewhere else and shows up as one in a diff.
var allowedOrigins = builder.Configuration
    .GetSection(ApiOptions.SectionName + ":AllowedOrigins")
    .Get<string[]>() ?? [];

if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.MapArronixApi();

if (app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value.PublishApiDescription)
{
    app.MapApiDescription();
}

// Last, so that nothing it serves can shadow a route: the client is whatever is left over.
app.UseArronixClient();

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// The entry point, made reachable so the test project can start this server in-process rather than
/// re-describing its wiring in a second place that could disagree with this one.
/// </summary>
public partial class Program;
