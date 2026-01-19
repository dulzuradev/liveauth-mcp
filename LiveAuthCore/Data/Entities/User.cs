namespace LiveAuthCore.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public bool IsSubscribed { get; set; }
        public string ApiKey { get; set; }
    }
}
