using EncDotNet.S100.Cli.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the web host the serving commands share (issue #1506): it must
/// put no file watcher on the working directory, where a rename event can
/// end the process.
/// </summary>
public sealed class ServeHostBuilderTests
{
    [Fact]
    public void ContentRootIsTheAppFolderNotTheWorkingDirectory()
    {
        var builder = ServeHost.CreateBuilder();

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            Path.TrimEndingDirectorySeparator(builder.Environment.ContentRootPath));
    }

    [Fact]
    public void ConfigurationFilesDoNotReloadOnChange()
    {
        var builder = ServeHost.CreateBuilder();

        var files = ((IConfigurationBuilder)builder.Configuration).Sources.OfType<FileConfigurationSource>().ToList();

        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.False(f.ReloadOnChange, $"{f.Path} reloads on change."));
    }

    [Fact]
    public async Task BuiltHostServesWithoutWatchingFiles()
    {
        var builder = ServeHost.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        var files = ((IConfigurationRoot)app.Configuration).Providers.OfType<FileConfigurationProvider>();
        Assert.All(files, f => Assert.False(f.Source.ReloadOnChange));
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            Path.TrimEndingDirectorySeparator(app.Services.GetRequiredService<IHostEnvironment>().ContentRootPath));
    }
}
