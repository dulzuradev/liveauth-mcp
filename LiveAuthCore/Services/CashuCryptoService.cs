using System.Security.Cryptography;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace LiveAuthCore.Services;

/// <summary>
/// Handles Cashu blind signature cryptographic operations
/// Note: This is a simplified implementation. For production use, consider using
/// a dedicated Cashu library or more robust blind signature implementation.
/// </summary>
public class CashuCryptoService
{
    /// <summary>
    /// Decomposes an amount into powers of 2
    /// Example: 13 = 8 + 4 + 1 = [1, 4, 8]
    /// </summary>
    public static List<long> DecomposeAmount(long amount)
    {
        var powers = new List<long>();
        long power = 1;
        
        while (amount > 0)
        {
            if ((amount & 1) == 1)
            {
                powers.Add(power);
            }
            amount >>= 1;
            power <<= 1;
        }
        
        return powers;
    }

    /// <summary>
    /// Generates a random secret (32 bytes hex-encoded)
    /// </summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Creates a blinded message B_ = Y + r*G
    /// where Y = hash_to_curve(secret), r = blinding factor
    /// Returns: (blinded_message_hex, secret, blinding_factor_hex)
    /// </summary>
    public static (string B_, string secret, string r) CreateBlindedMessage()
    {
        var secret = GenerateSecret();
        
        // Generate blinding factor (private key)
        var blindingKey = new Key();
        var r = blindingKey.ToHex();
        
        // Hash secret to curve point Y
        var Y = HashToCurve(secret);
        
        // Compute B_ = Y + r*G
        var rG = blindingKey.PubKey.ToBytes();
        
        // For simplicity, we'll use the hash of both as the blinded message
        // In a real implementation, this should be proper EC point addition
        var B_ = ComputeBlindedPoint(Y, rG);
        
        return (B_, secret, r);
    }

    /// <summary>
    /// Unblinds a signature: C = C_ - r*K
    /// where C_ is the blinded signature from the mint, K is the mint's public key
    /// </summary>
    public static string UnblindSignature(string C_, string r, string K)
    {
        // Parse inputs
        var blindedSig = ParseHexPoint(C_);
        var blindingFactor = new Key(Encoders.Hex.DecodeData(r));
        var mintPubKey = new PubKey(K);
        
        // Compute r*K
        var rK = DeriveSharedKey(blindingFactor, mintPubKey);
        
        // C = C_ - r*K (simplified: XOR for demonstration)
        // In production, use proper EC subtraction
        var C = ComputeUnblindedPoint(blindedSig, rK);
        
        return C;
    }

    /// <summary>
    /// Hashes a secret string to a curve point representation
    /// </summary>
    public static byte[] HashToCurve(string secret)
    {
        var messageBytes = System.Text.Encoding.UTF8.GetBytes(secret);
        return SHA256.HashData(messageBytes);
    }

    /// <summary>
    /// Parses a hex point (simplified)
    /// </summary>
    private static byte[] ParseHexPoint(string hexPoint)
    {
        try
        {
            return Convert.FromHexString(hexPoint);
        }
        catch
        {
            // If it's a compressed pubkey format
            return new PubKey(hexPoint).ToBytes();
        }
    }

    /// <summary>
    /// Computes blinded point (simplified implementation)
    /// In production, use proper EC point addition
    /// </summary>
    private static string ComputeBlindedPoint(byte[] Y, byte[] rG)
    {
        // Simplified: combine with XOR for demonstration
        // Real implementation needs EC point addition
        var result = new byte[Math.Max(Y.Length, rG.Length)];
        for (int i = 0; i < result.Length; i++)
        {
            byte yByte = i < Y.Length ? Y[i] : (byte)0;
            byte rByte = i < rG.Length ? rG[i] : (byte)0;
            result[i] = (byte)(yByte ^ rByte);
        }
        return Convert.ToHexString(result).ToLowerInvariant();
    }

    /// <summary>
    /// Computes unblinded point (simplified implementation)
    /// </summary>
    private static string ComputeUnblindedPoint(byte[] C_, byte[] rK)
    {
        // Simplified: combine with XOR for demonstration
        var result = new byte[Math.Max(C_.Length, rK.Length)];
        for (int i = 0; i < result.Length; i++)
        {
            byte cByte = i < C_.Length ? C_[i] : (byte)0;
            byte rByte = i < rK.Length ? rK[i] : (byte)0;
            result[i] = (byte)(cByte ^ rByte);
        }
        return Convert.ToHexString(result).ToLowerInvariant();
    }

    /// <summary>
    /// Derives a shared key (used for computing r*K)
    /// </summary>
    private static byte[] DeriveSharedKey(Key privateKey, PubKey publicKey)
    {
        // Use ECDH to compute shared secret
        var sharedSecret = publicKey.GetSharedPubkey(privateKey);
        return sharedSecret.ToBytes();
    }
}

/// <summary>
/// Represents a blinded message for minting
/// </summary>
public class BlindedMessage
{
    public long Amount { get; set; }
    public string Id { get; set; } = string.Empty; // keyset_id
    public string B_ { get; set; } = string.Empty; // blinded point (hex)
}

/// <summary>
/// Represents a blinded signature from the mint
/// </summary>
public class BlindedSignature
{
    public long Amount { get; set; }
    public string Id { get; set; } = string.Empty; // keyset_id
    public string C_ { get; set; } = string.Empty; // blinded signature (hex)
}

/// <summary>
/// Represents an ecash proof
/// </summary>
public class CashuProof
{
    public long Amount { get; set; }
    public string Id { get; set; } = string.Empty; // keyset_id
    public string Secret { get; set; } = string.Empty;
    public string C { get; set; } = string.Empty; // unblinded signature (hex)
}
