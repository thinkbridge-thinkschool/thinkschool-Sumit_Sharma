using OrderApi.Models;
using OrderApi.Repositories;

namespace OrderApi.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository repository;
    private readonly ILogger<OrderService> logger;

    public OrderService(
        IOrderRepository repository,
        ILogger<OrderService> logger)
    {
        this.repository = repository;
        this.logger = logger;
    }

    public async Task<Order> CreateOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        if (order.Items.Count == 0)
            throw new ArgumentException("Order must contain at least one item.");

        if (order.Items.Any(item => item.Quantity <= 0))
            throw new ArgumentException("Item quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(order.Customer.Name))
            throw new ArgumentException("Customer name is required.");

        if (string.IsNullOrWhiteSpace(order.Customer.Email))
            throw new ArgumentException("Customer email is required.");

        decimal total = order.Items.Sum(
            item => item.Price * item.Quantity);

        if (total > 1000)
            total *= 0.9m;

        if (order.Items.Count > 10)
            total += 50;

        order.Total = total;
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