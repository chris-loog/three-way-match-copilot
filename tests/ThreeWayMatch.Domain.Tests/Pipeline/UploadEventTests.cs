using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Infrastructure.Pipeline;

namespace ThreeWayMatch.Domain.Tests.Pipeline;

public class UploadEventTests
{
    private static string Message(string subject, string eventType = "Microsoft.Storage.BlobCreated") =>
        $$$"""{"topic":"x","subject":"{{{subject}}}","eventType":"{{{eventType}}}","data":{}}""";

    [Theory]
    [InlineData("invoices/INV-1.pdf", DocumentType.Invoice)]
    [InlineData("Invoices/INV-1.pdf", DocumentType.Invoice)]
    [InlineData("delivery-notes/DN-7.jpg", DocumentType.DeliveryNote)]
    public void Reads_the_blob_and_takes_the_type_from_the_folder(string name, DocumentType type)
    {
        var ok = UploadEvent.TryParse(Message($"/blobServices/default/containers/incoming/blobs/{name}"), out var upload, out _);

        Assert.True(ok);
        Assert.Equal(type, upload!.Type);
        Assert.Equal($"incoming/{name}", upload.BlobPath);
    }

    [Fact]
    public void Reads_a_cloud_events_message_too()
    {
        var message = """{"type":"Microsoft.Storage.BlobCreated","subject":"/blobServices/default/containers/incoming/blobs/invoices/a.pdf"}""";

        Assert.True(UploadEvent.TryParse(message, out var upload, out _));
        Assert.Equal("incoming/invoices/a.pdf", upload!.BlobPath);
    }

    [Theory]
    [InlineData("/blobServices/default/containers/incoming/blobs/other/a.pdf", "Microsoft.Storage.BlobCreated")]
    [InlineData("/blobServices/default/containers/incoming/blobs/a.pdf", "Microsoft.Storage.BlobCreated")]
    [InlineData("/blobServices/default/containers/training/blobs/invoices/a.pdf", "Microsoft.Storage.BlobCreated")]
    [InlineData("/blobServices/default/containers/incoming/blobs/invoices/a.pdf", "Microsoft.Storage.BlobDeleted")]
    [InlineData("nonsense", "Microsoft.Storage.BlobCreated")]
    public void Refuses_messages_that_can_never_succeed(string subject, string eventType)
    {
        Assert.False(UploadEvent.TryParse(Message(subject, eventType), out var upload, out var reason));
        Assert.Null(upload);
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void Refuses_text_that_is_not_json()
    {
        Assert.False(UploadEvent.TryParse("not json", out _, out var reason));
        Assert.Contains("JSON", reason, StringComparison.Ordinal);
    }
}
