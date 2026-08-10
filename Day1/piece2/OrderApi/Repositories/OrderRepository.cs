using OrderApi.Models;

namespace OrderApi.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ILogger<OrderRepository> logger;

    public OrderRepository(ILogger<OrderRepository> logger)
    {
        this.logger = logger;
    }

    public async Task<Order> AddAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);

        logger.LogInformation(
            "Order {OrderId} stored successfully",
            order.Id);

        return order;
    }
}