namespace LiveAuthCore.Services;

public static class BasisPointFeeMath
{
    public static long CalculateFeeSats(long amountSats, int feeBasisPoints, long minimumFeeSats)
    {
        if (amountSats <= 0 || feeBasisPoints <= 0)
            return 0;

        var minimum = Math.Max(0, minimumFeeSats);
        var percentageFee = amountSats * feeBasisPoints / 10_000;
        return Math.Max(minimum, percentageFee);
    }
}
