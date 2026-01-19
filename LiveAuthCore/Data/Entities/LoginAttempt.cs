namespace LiveAuthCore.Entities
{
    public class LoginAttempt
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string PaymentHash { get; set; } // Hex payment hash
        public bool IsSuccessful { get; set; }
        public DateTime AttemptTime { get; set; }
        public bool IsRefunded { get; set; } // Tracks if sats were refunded
        public string? RefundPaymentHash { get; set; } // Hash of refund invoice (if any)
    }
}
