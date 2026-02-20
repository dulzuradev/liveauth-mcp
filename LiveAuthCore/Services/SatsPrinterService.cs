using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
// using NBitcoin.Secp256k1;

namespace LiveAuthCore.Services;

public class SatsPrinterService
{
    private readonly HttpClient _httpClient;
    private readonly LiveAuthDbContext _dbContext;
    private readonly LightningService _lightningService;
    private readonly ILogger<SatsPrinterService> _logger;
    private const string DefaultMintUrl = "https://mint.minibits.cash/Bitcoin";

    public SatsPrinterService(
        HttpClient httpClient,
        LiveAuthDbContext dbContext,
        LightningService lightningService,
        ILogger<SatsPrinterService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _lightningService = lightningService;
        _logger = logger;
    }

    /// <summary>
    /// NUT-04: Mint ecash tokens by paying a Lightning invoice
    /// </summary>
    public async Task<MintRequest> MintSatsAsync(string userId, long amount, string mintUrl)
    {
        mintUrl = string.IsNullOrEmpty(mintUrl) ? DefaultMintUrl : mintUrl;
        _logger.LogInformation("Starting NUT-04 mint for {UserId}, Amount: {Amount}, Mint: {MintUrl}", 
            userId, amount, mintUrl);

        // 1. Create DB Record
        var request = new MintRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MintUrl = mintUrl,
            Amount = amount,
            Status = MintRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.MintRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        try
        {
            // 2. Get active keyset
            var keysetId = await GetActiveKeysetAsync(mintUrl);
            var mintKeys = await GetKeysetKeysAsync(mintUrl, keysetId);
            
            // 3. Create mint quote (NUT-04 Step 1)
            var quoteRequest = new MintQuoteRequest { Amount = amount, Unit = "sat" };
            var quoteResponse = await _httpClient.PostAsJsonAsync(
                $"{mintUrl}/v1/mint/quote/bolt11", quoteRequest);
            
            if (!quoteResponse.IsSuccessStatusCode)
            {
                var errorContent = await quoteResponse.Content.ReadAsStringAsync();
                _logger.LogError("Mint quote API error: {Error}", errorContent);
                request.Status = MintRequestStatus.Failed;
                await _dbContext.SaveChangesAsync();
                throw new Exception($"Failed to get mint quote: {errorContent}");
            }

            var mintQuote = await quoteResponse.Content.ReadFromJsonAsync<MintQuoteResponse>();
            
            if (mintQuote == null || string.IsNullOrEmpty(mintQuote.Quote) || string.IsNullOrEmpty(mintQuote.Request))
            {
                request.Status = MintRequestStatus.Failed;
                await _dbContext.SaveChangesAsync();
                throw new Exception("Invalid mint quote response structure");
            }

            request.Invoice = mintQuote.Request;
            request.PaymentHash = mintQuote.Quote;
            request.Status = MintRequestStatus.Processing;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Mint quote created: {Quote}, Invoice: {Invoice}", 
                mintQuote.Quote, mintQuote.Request);

            // 4. Pay the Lightning invoice
            _logger.LogInformation("Paying invoice via LightningService...");
            await _lightningService.PayInvoice(request.Invoice);

            // 5. Wait for payment confirmation (poll quote status)
            var paid = await WaitForQuotePaidAsync(mintUrl, mintQuote.Quote);
            if (!paid)
            {
                request.Status = MintRequestStatus.Failed;
                await _dbContext.SaveChangesAsync();
                throw new Exception("Quote payment timeout or failed");
            }

            // 6. Generate blinded messages (NUT-00)
            var amounts = CashuCryptoService.DecomposeAmount(amount);
            var blindedOutputs = new List<(BlindedMessage message, string secret, string r)>();
            
            foreach (var amt in amounts)
            {
                var (B_, secret, r) = CashuCryptoService.CreateBlindedMessage();
                var blindedMessage = new BlindedMessage
                {
                    Amount = amt,
                    Id = keysetId,
                    B_ = B_
                };
                blindedOutputs.Add((blindedMessage, secret, r));
            }

            // 7. Mint tokens (NUT-04 Step 2)
            var mintRequest = new MintBolt11Request
            {
                Quote = mintQuote.Quote,
                Outputs = blindedOutputs.Select(o => o.message).ToList()
            };

            var mintResponse = await _httpClient.PostAsJsonAsync(
                $"{mintUrl}/v1/mint/bolt11", mintRequest);
            
            if (!mintResponse.IsSuccessStatusCode)
            {
                var errorContent = await mintResponse.Content.ReadAsStringAsync();
                _logger.LogError("Mint bolt11 API error: {Error}", errorContent);
                request.Status = MintRequestStatus.Failed;
                await _dbContext.SaveChangesAsync();
                throw new Exception($"Failed to mint tokens: {errorContent}");
            }

            var mintResult = await mintResponse.Content.ReadFromJsonAsync<MintBolt11Response>();
            
            if (mintResult == null || mintResult.Signatures == null || mintResult.Signatures.Count != blindedOutputs.Count)
            {
                request.Status = MintRequestStatus.Failed;
                await _dbContext.SaveChangesAsync();
                throw new Exception("Invalid mint response: signature count mismatch");
            }

            // 8. Unblind signatures and store proofs
            for (int i = 0; i < blindedOutputs.Count; i++)
            {
                var (blindedMsg, secret, r) = blindedOutputs[i];
                var signature = mintResult.Signatures[i];
                
                // Get the mint's public key for this amount
                var mintPubKeyHex = mintKeys.GetValueOrDefault(blindedMsg.Amount.ToString());
                if (string.IsNullOrEmpty(mintPubKeyHex))
                {
                    throw new Exception($"No mint public key for amount {blindedMsg.Amount}");
                }

                var K = mintPubKeyHex; // hex pubkey from mint
                var C_ = signature.C_; // blinded signature hex
                
                // Unblind: C = C_ - r*K (simplified in CashuCryptoService)
                var C = CashuCryptoService.UnblindSignature(C_, r, K);
                
                // Store the proof in database
                var proof = new EcashProof
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    MintUrl = mintUrl,
                    Amount = blindedMsg.Amount,
                    KeysetId = keysetId,
                    Secret = secret,
                    C = C,
                    IsSpent = false,
                    CreatedAt = DateTime.UtcNow,
                    MintRequestId = request.Id
                };
                _dbContext.EcashProofs.Add(proof);
            }

            // 9. Update user balance
            await UpdateUserBalanceAsync(userId, mintUrl);

            request.Status = MintRequestStatus.Completed;
            request.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Mint completed successfully: {RequestId}, Proofs: {Count}", 
                request.Id, amounts.Count);

            return request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing mint request {RequestId}", request.Id);
            request.Status = MintRequestStatus.Failed;
            request.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// NUT-05: Melt ecash tokens to pay a Lightning invoice
    /// </summary>
    public async Task<MeltBolt11Response> MeltSatsAsync(string userId, string invoice, string mintUrl)
    {
        mintUrl = string.IsNullOrEmpty(mintUrl) ? DefaultMintUrl : mintUrl;
        _logger.LogInformation("Starting NUT-05 melt for {UserId}, Mint: {MintUrl}", userId, mintUrl);

        // 1. Get melt quote
        var quoteRequest = new MeltQuoteRequest { Request = invoice, Unit = "sat" };
        var quoteResponse = await _httpClient.PostAsJsonAsync(
            $"{mintUrl}/v1/melt/quote/bolt11", quoteRequest);
        
        if (!quoteResponse.IsSuccessStatusCode)
        {
            var errorContent = await quoteResponse.Content.ReadAsStringAsync();
            throw new Exception($"Failed to get melt quote: {errorContent}");
        }

        var meltQuote = await quoteResponse.Content.ReadFromJsonAsync<MeltQuoteResponse>();
        if (meltQuote == null)
        {
            throw new Exception("Invalid melt quote response");
        }

        var totalNeeded = meltQuote.Amount + meltQuote.FeeReserve;
        _logger.LogInformation("Melt quote: Amount={Amount}, Fee={Fee}, Total={Total}", 
            meltQuote.Amount, meltQuote.FeeReserve, totalNeeded);

        // 2. Select unspent proofs to cover the amount + fee
        var proofs = await SelectProofsAsync(userId, mintUrl, totalNeeded);
        if (proofs.Sum(p => p.Amount) < totalNeeded)
        {
            throw new Exception($"Insufficient balance. Need {totalNeeded} sats, have {proofs.Sum(p => p.Amount)}");
        }

        // 3. Convert to Cashu proofs
        var cashuProofs = proofs.Select(p => new CashuProof
        {
            Amount = p.Amount,
            Id = p.KeysetId,
            Secret = p.Secret,
            C = p.C
        }).ToList();

        // 4. Melt the tokens
        var meltRequest = new MeltBolt11Request
        {
            Quote = meltQuote.Quote,
            Inputs = cashuProofs
        };

        var meltResponse = await _httpClient.PostAsJsonAsync(
            $"{mintUrl}/v1/melt/bolt11", meltRequest);
        
        if (!meltResponse.IsSuccessStatusCode)
        {
            var errorContent = await meltResponse.Content.ReadAsStringAsync();
            throw new Exception($"Failed to melt tokens: {errorContent}");
        }

        var meltResult = await meltResponse.Content.ReadFromJsonAsync<MeltBolt11Response>();
        if (meltResult == null)
        {
            throw new Exception("Invalid melt response");
        }

        // 5. Mark proofs as spent
        foreach (var proof in proofs)
        {
            proof.IsSpent = true;
            proof.SpentAt = DateTime.UtcNow;
        }

        // 6. If there's change, store it as new proofs
        // (In a real implementation, you'd need to handle the change blinded signatures)
        // For now, we'll skip change handling as it requires additional blind signature logic

        // 7. Update user balance
        await UpdateUserBalanceAsync(userId, mintUrl);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Melt completed successfully for {UserId}, Paid: {Paid}", userId, meltResult.Paid);

        return meltResult;
    }

    /// <summary>
    /// Get user's ecash balance across all mints
    /// </summary>
    public async Task<Dictionary<string, long>> GetUserBalanceAsync(string userId)
    {
        var balances = await _dbContext.EcashProofs
            .Where(p => p.UserId == userId && !p.IsSpent)
            .GroupBy(p => p.MintUrl)
            .Select(g => new { MintUrl = g.Key, Balance = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.MintUrl, x => x.Balance);

        return balances;
    }

    private async Task<string> GetActiveKeysetAsync(string mintUrl)
    {
        var response = await _httpClient.GetFromJsonAsync<KeysetsResponse>($"{mintUrl}/v1/keysets");
        var activeKeyset = response?.Keysets?.FirstOrDefault(k => k.Active && k.Unit == "sat");
        
        if (activeKeyset == null)
        {
            throw new Exception("No active keyset found for mint");
        }

        return activeKeyset.Id;
    }

    private async Task<Dictionary<string, string>> GetKeysetKeysAsync(string mintUrl, string keysetId)
    {
        var response = await _httpClient.GetFromJsonAsync<KeysResponse>($"{mintUrl}/v1/keys/{keysetId}");
        var keyset = response?.Keysets?.FirstOrDefault(k => k.Id == keysetId);
        return keyset?.Keys ?? new Dictionary<string, string>();
    }

    private async Task<bool> WaitForQuotePaidAsync(string mintUrl, string quoteId, int maxAttempts = 30)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var response = await _httpClient.GetFromJsonAsync<MintQuoteStatusResponse>(
                $"{mintUrl}/v1/mint/quote/bolt11/{quoteId}");
            
            if (string.Equals(response?.State, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(response?.State, "ISSUED", StringComparison.OrdinalIgnoreCase))
            {
                // Already issued, shouldn't happen but we can proceed
                return true;
            }

            // Some mints may not set State reliably; fall back to Paid boolean.
            if (response?.Paid == true)
            {
                return true;
            }

            await Task.Delay(2000); // Wait 2 seconds before retry
        }

        return false;
    }

    private async Task<List<EcashProof>> SelectProofsAsync(string userId, string mintUrl, long amount)
    {
        var allProofs = await _dbContext.EcashProofs
            .Where(p => p.UserId == userId && p.MintUrl == mintUrl && !p.IsSpent)
            .OrderBy(p => p.Amount)
            .ToListAsync();

        var selected = new List<EcashProof>();
        long total = 0;

        foreach (var proof in allProofs)
        {
            selected.Add(proof);
            total += proof.Amount;

            if (total >= amount)
            {
                break;
            }
        }

        return selected;
    }

    private async Task UpdateUserBalanceAsync(string userId, string mintUrl)
    {
        var balance = await _dbContext.EcashProofs
            .Where(p => p.UserId == userId && p.MintUrl == mintUrl && !p.IsSpent)
            .SumAsync(p => p.Amount);

        var existing = await _dbContext.UserEcashBalances
            .FirstOrDefaultAsync(b => b.UserId == userId && b.MintUrl == mintUrl);

        if (existing != null)
        {
            existing.Balance = balance;
            existing.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            _dbContext.UserEcashBalances.Add(new UserEcashBalance
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MintUrl = mintUrl,
                Balance = balance,
                LastUpdated = DateTime.UtcNow
            });
        }
    }
}
