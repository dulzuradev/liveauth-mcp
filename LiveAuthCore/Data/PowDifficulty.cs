public static class PowDifficulty
{
    public static byte[] TargetFromBits(int bits)
    {
        // bits = number of leading zero bits required
        var bytes = new byte[32];
        var fullZeroBytes = bits / 8;
        var remainingBits = bits % 8;

        for (int i = 0; i < fullZeroBytes; i++)
            bytes[i] = 0x00;

        if (remainingBits > 0 && fullZeroBytes < 32)
            bytes[fullZeroBytes] = (byte)(0xFF >> remainingBits);

        for (int i = fullZeroBytes + 1; i < 32; i++)
            bytes[i] = 0xFF;

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