using OrderApi.Models;
using OrderApi.Repositories;
using OrderApi.Strategies;

namespace OrderApi.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository repository;
    private readonly IOrderPricingStrategy pricingStrategy;
    private readonly ILogger logger;

    public OrderService(
        IOrderRepository repository,
        IOrderPricingStrategy pricingStrategy,
        ILogger<OrderService> logger)
    {
        this.repository = repository;
        this.pricingStrategy = pricingStrategy;
        this.logger = logger;
    }

    public async Task<Order> CreateOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        if (order.Items.Count == 0)
            throw new ArgumentException(
                "Order must contain at least one item.");

        if (order.Items.Any(item => item.Quantity <= 0))
            throw new ArgumentException(
                "Item quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(order.Customer.Name))
            throw new ArgumentException(
                "Customer name is required.");

        if (string.IsNullOrWhiteSpace(order.Customer.Email))
            throw new ArgumentException(
                "Customer email is required.");

        order.Total = pricingStrategy.CalculateTotal(order);
        order.Status = "Created";

        logger.LogInformation(
            "Creating order for customer {CustomerName} with total {Total}",
            order.Customer.Name,
            order.Total);

        return await repository.AddAsync(
            order,
            cancellationToken);
    }
}
