using OrderApi.Models;

namespace OrderApi.Services;

public interface IOrderService
{
    Task<Order> CreateOrderAsync(
        Order order,
        CancellationToken cancellationToken);
}