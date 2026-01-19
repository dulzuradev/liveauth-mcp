namespace LiveAuthCore.Entities
{
    public class Transaction
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string PaymentHash { get; set; }
        public long AmountSats { get; set; }
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
