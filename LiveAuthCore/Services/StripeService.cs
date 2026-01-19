namespace LiveAuthCore.Services
{
    using Stripe;
    using Stripe.Checkout;
    public class StripeService
    {
        public StripeService(IConfiguration configuration)
        {
            StripeConfiguration.ApiKey = configuration["Stripe:ApiKey"];
        }
        public async Task<string> CreateSubscriptionAsync(string customerId, string priceId)
        {
            var options = new SubscriptionCreateOptions
            {
                Customer = customerId,
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions { Price = priceId }
                }
            };
            var service = new SubscriptionService();
            //var subscription = await service.CreateAsync(options);
            var subscription = new { Id = "123" };
            return subscription.Id;
        }

        public object Id { get; set; }

        public async Task<string> CreateCheckoutSessionAsync(string successUrl, string cancelUrl, string priceId)
        {
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                Mode = "subscription",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl
            };
            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return session.Url;
        }
    }

}
