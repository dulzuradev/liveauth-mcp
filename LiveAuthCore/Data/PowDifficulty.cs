public static class PowDifficulty
{
    public static byte[] TargetFromBits(int bits)
    {
        if (bits < 0 || bits > 256)
            throw new ArgumentOutOfRangeException(nameof(bits), "Difficulty bits must be between 0 and 256.");

        // bits = number of leading zero bits required
        var bytes = Enumerable.Repeat((byte)0xFF, 32).ToArray();
        var fullZeroBytes = bits / 8;
        var remainingBits = bits % 8;

        for (int i = 0; i < fullZeroBytes; i++)
            bytes[i] = 0x00;

        if (remainingBits > 0 && fullZeroBytes < 32)
            bytes[fullZeroBytes] = (byte)(0xFF >> remainingBits);

        return bytes;
    }

    public static bool IsValid(byte[] hash, byte[] target)
    {
        for (int i = 0; i < 32; i++)
        {
            if (hash[i] < target[i]) return true;
            if (hash[i] > target[i]) return false;
        }
        return true;
    }
}
