namespace ZLinq.Tests.Linq;

public class ToListTest
{
    [Fact]
    public void Empty()
    {
        var xs = new int[0];

        var actual = xs.AsValueEnumerable(); // TODO:Do
    }

    [Fact]
    public void NonEmpty()
    {
        var xs = new int[] { 1, 2, 3, 4, 5 };

        var actual = xs.AsValueEnumerable(); // TODO:Do
    }

}
