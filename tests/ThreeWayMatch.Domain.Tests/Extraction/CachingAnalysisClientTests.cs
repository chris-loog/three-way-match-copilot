using System.Text;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;

namespace ThreeWayMatch.Domain.Tests.Extraction;

public sealed class CachingAnalysisClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "twm-cache-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class CountingClient : IAnalysisClient
    {
        public int Calls { get; private set; }

        public Task<string> AnalyzeAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult($"{{\"call\":{Calls}}}");
        }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task Analyses_a_document_once_and_serves_the_second_request_from_the_cache()
    {
        var inner = new CountingClient();
        var cache = new CachingAnalysisClient(inner, _directory, "prebuilt-invoice");

        var first = await cache.AnalyzeAsync(Bytes("invoice A"), CancellationToken.None);
        var second = await cache.AnalyzeAsync(Bytes("invoice A"), CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Different_content_is_analysed_separately()
    {
        var inner = new CountingClient();
        var cache = new CachingAnalysisClient(inner, _directory, "prebuilt-invoice");

        await cache.AnalyzeAsync(Bytes("invoice A"), CancellationToken.None);
        await cache.AnalyzeAsync(Bytes("invoice B"), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task The_cache_file_is_named_after_the_content_hash_and_the_model()
    {
        var cache = new CachingAnalysisClient(new CountingClient(), _directory, "prebuilt-invoice");
        var content = Bytes("invoice A");

        await cache.AnalyzeAsync(content, CancellationToken.None);

        var expected = Path.Combine(_directory, $"{ContentHash.Sha256Hex(content)}.prebuilt-invoice.json");
        Assert.True(File.Exists(expected));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task A_different_model_does_not_reuse_another_models_result()
    {
        var inner = new CountingClient();
        await new CachingAnalysisClient(inner, _directory, "prebuilt-invoice").AnalyzeAsync(Bytes("invoice A"), CancellationToken.None);
        await new CachingAnalysisClient(inner, _directory, "delivery-note-neural-v1").AnalyzeAsync(Bytes("invoice A"), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Survives_a_new_instance_because_the_cache_lives_on_disk()
    {
        var inner = new CountingClient();
        await new CachingAnalysisClient(inner, _directory, "prebuilt-invoice").AnalyzeAsync(Bytes("invoice A"), CancellationToken.None);
        await new CachingAnalysisClient(inner, _directory, "prebuilt-invoice").AnalyzeAsync(Bytes("invoice A"), CancellationToken.None);

        Assert.Equal(1, inner.Calls);
    }
}
