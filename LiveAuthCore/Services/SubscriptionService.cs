
namespace LiveAuthCore.Services
{
    public interface ISubscriptionService
    {
        Task<bool> IsSubscribed(object userId);
    }
    public class SubscriptionService : ISubscriptionService
    {
        Task<bool> ISubscriptionService.IsSubscribed(object userId)
        {
            throw new NotImplementedException();
        }

       
    }
}
