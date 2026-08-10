using OrderApi.Models;

namespace OrderApi.Repositories;

public interface IOrderRepository
{
    Task<Order> AddAsync(
        Order order,
        CancellationToken cancellationToken);
}
