using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ThreeWayMatch.Web.Health;

namespace ThreeWayMatch.Domain.Tests.Web;

public class HealthTests
{
    [Fact]
    public async Task Health_answers_200_without_signing_in_and_names_each_check()
    {
        using var world = new WebWorld();

        var response = await world.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        var checks = json.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetProperty("status").GetString());
        Assert.Equal("Healthy", checks.GetProperty("storage").GetProperty("status").GetString());
        Assert.Equal("Healthy", checks.GetProperty("queue").GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_storage_account_that_cannot_be_reached_makes_health_unhealthy_without_leaking_the_error()
    {
        // Port 1 on localhost refuses connections at once, so the check fails fast.
        var services = new ServiceProviderStub(new BlobServiceClient(new Uri("http://127.0.0.1:1/devstoreaccount1"), new Azure.Storage.Blobs.BlobClientOptions
        {
            Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(2) },
        }));

        var result = await new StorageCheck(services).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("The upload container cannot be read.", result.Description);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task A_queue_that_is_not_configured_is_skipped()
    {
        var result = await new QueueCheck(new ServiceProviderStub()).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private sealed class ServiceProviderStub(params object[] instances) : IServiceProvider
    {
        public object? GetService(Type serviceType) => instances.FirstOrDefault(serviceType.IsInstanceOfType);
    }
}
