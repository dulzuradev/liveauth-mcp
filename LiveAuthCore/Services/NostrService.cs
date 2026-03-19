using System.Security.Cryptography;
using System.Text;

namespace LiveAuthCore.Services;

/// <summary>
/// Service for Nostr identity verification using Schnorr signatures
/// NOTE: This uses a simplified verification. For production, use a proper secp256k1 library.
/// </summary>
public class NostrService
{
    private const string ChallengePrefix = "LiveAuth:verify:";

    /// <summary>
    /// Generate a verification challenge for a Nostr npub
    /// </summary>
    public string GenerateChallenge(string sessionId)
    {
        return $"{ChallengePrefix}{sessionId}";
    }

    /// <summary>
    /// Verify a Schnorr signature against a message
    /// NOTE: This is a placeholder - returns true for testing. 
    /// In production, use a proper secp256k1 library (e.g., nostr-sdk-net or verify server-side)
    /// </summary>
    public bool VerifySignature(string signatureHex, string message, string npubHex)
    {
        try
        {
            // Validate inputs
            var sigBytes = HexStringToBytes(signatureHex);
            if (sigBytes.Length != 64)
                return false;

            var pubkeyBytes = HexStringToBytes(npubHex);
            if (pubkeyBytes.Length != 32)
                return false;

            // TODO: Replace with proper secp256k1 verification
            // For now, accept any valid-format signature
            // In production, use: nostr-sdk-net, NBitcoin.Secp256k1 (internal), or nostr-verifier API
            
            // Basic format check - real implementation would verify the signature cryptographically
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Convert npub to hex public key (Bech32 decode)
    /// </summary>
    public string NpubToHex(string npub)
    {
        try
        {
            var decoded = Bech32Decode(npub, "npub");
            return BytesToHex(decoded);
        }
        catch
        {
            if (npub.Length == 64 && IsHex(npub))
                return npub.ToLower();
            
            throw new ArgumentException("Invalid npub format");
        }
    }

    /// <summary>
    /// Convert hex public key to npub (Bech32 encode)
    /// </summary>
    public string HexToNpub(string hexPubkey)
    {
        var pubkeyBytes = HexStringToBytes(hexPubkey);
        return Bech32Encode(pubkeyBytes, "npub");
    }

    private static byte[] HexStringToBytes(string hex)
    {
        hex = hex.ToLower().Replace(" ", "").Replace("-", "");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static string BytesToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLower();
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value.ToLower())
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }

    private static readonly string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    
    private static byte[] Bech32Decode(string bech, string prefix)
    {
        if (bech.Length < 6)
            throw new ArgumentException("Too short");
            
        var pos = bech.LastIndexOf('1');
        if (pos < 1)
            throw new ArgumentException("Invalid separator");
            
        var prefixPart = bech[..pos];
        if (prefixPart != prefix)
            throw new ArgumentException($"Invalid prefix: {prefixPart}");
            
        var dataPart = bech[(pos + 1)..].ToLower();
        
        var data = new List<byte>();
        foreach (char c in dataPart)
        {
            var idx = Charset.IndexOf(c);
            if (idx < 0)
                throw new ArgumentException($"Invalid character: {c}");
            data.Add((byte)idx);
        }
        
        return data.Take(data.Count - 6).ToArray();
    }

    private static string Bech32Encode(byte[] data, string prefix)
    {
        var base32 = Base32Encode(data);
        var checksum = ComputeChecksum(data);
        return (prefix + "1" + base32 + checksum).ToLower();
    }

    private static string ComputeChecksum(byte[] data)
    {
        var values = data.Select(b => (int)b).ToList();
        values.AddRange(new[] { 0, 0, 0, 0, 0, 0 });
        
        var GEN = new[] { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };
        
        var chk = 1;
        foreach (var value in values)
        {
            var top = chk >> 25;
            chk = (chk & 0x1ffffff) << 5 ^ value;
            for (int i = 0; i < 6; i++)
            {
                if (((top >> i) & 1) != 0)
                    chk ^= GEN[i];
            }
        }
        
        var result = new char[6];
        for (int i = 0; i < 6; i++)
        {
            result[i] = Charset[(chk >> (5 * (5 - i))) & 31];
        }
        
        return new string(result);
    }

    private static byte[] Base32Decode(string input)
    {
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;
        
        foreach (var c in input.ToUpper())
        {
            var value = Charset.IndexOf(char.ToLower(c));
            if (value < 0) continue;
            
            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }
        
        return output.ToArray();
    }

    private static string Base32Encode(byte[] input)
    {
        var output = new System.Text.StringBuilder();
        var buffer = 0;
        var bitsLeft = 0;
        
        foreach (var b in input)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            
            while (bitsLeft >= 5)
            {
                output.Append(Charset[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }
        
        if (bitsLeft > 0)
        {
            output.Append(Charset[(buffer << (5 - bitsLeft)) & 31]);
        }
        
        return output.ToString();
    }
}
