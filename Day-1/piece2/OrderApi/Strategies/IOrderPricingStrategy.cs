using OrderApi.Models;

namespace OrderApi.Strategies;

public interface IOrderPricingStrategy
{
    decimal CalculateTotal(Order order);
}
