using System.Text;
using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Domain.Tests.Extraction;

public class ContentHashTests
{
    [Fact]
    public void Matches_the_published_sha256_test_vector_for_abc()
    {
        var hash = ContentHash.Sha256Hex(Encoding.ASCII.GetBytes("abc"));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void Gives_the_same_hash_for_the_same_bytes_and_a_different_one_otherwise()
    {
        byte[] a = [1, 2, 3];
        byte[] b = [1, 2, 4];

        Assert.Equal(ContentHash.Sha256Hex(a), ContentHash.Sha256Hex([1, 2, 3]));
        Assert.NotEqual(ContentHash.Sha256Hex(a), ContentHash.Sha256Hex(b));
        Assert.Equal(64, ContentHash.Sha256Hex(a).Length);
    }
}
