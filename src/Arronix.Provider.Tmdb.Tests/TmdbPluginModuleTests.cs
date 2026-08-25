using System;
using System.Net.Http;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Movies;
using Arronix.Provider.Tmdb.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests;

[TestFixture]
public sealed class TmdbPluginModuleTests
{
    [Test]
    public void Configure_registers_exactly_one_cataloger_and_one_curator_naming_Movie_once()
    {
        var registry = new RecordingPluginRegistry();
        var gateway = new TestHttpGateway(new HttpClient(
            new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("Configure must not call the network."))));
        var context = new TestPluginContext(gateway, new FakeTimeProvider(TestHarness.DefaultNow), registry);

        new TmdbPluginModule().Configure(context);

        registry.Catalogers.Should().ContainSingle();
        registry.Catalogers[0].ItemType.Should().Be(typeof(Movie));
        registry.Catalogers[0].ImplementationType.Should().Be(typeof(TmdbMovieCataloger));

        registry.Curators.Should().ContainSingle();
        registry.Curators[0].ItemType.Should().Be(typeof(Movie));
        registry.Curators[0].ImplementationType.Should().Be(typeof(TmdbMovieCurator));
    }

    [Test]
    public void Configure_rejects_a_null_context()
    {
        FluentActions.Invoking(() => new TmdbPluginModule().Configure(null!))
            .Should().Throw<ArgumentNullException>();
    }
}
