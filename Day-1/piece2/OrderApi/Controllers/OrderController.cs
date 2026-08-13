using Microsoft.AspNetCore.Mvc;
using OrderApi.Models;
using OrderApi.Services;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/orders")]
public class OrderController : ControllerBase
{
    private readonly IOrderService service;

    public OrderController(IOrderService service)
    {
        this.service = service;
    }

    [HttpPost]
    public async Task<ActionResult<Order>> Create(
        Order order,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await service.CreateOrderAsync(
                order,
                cancellationToken);

            return Created(
                $"/api/orders/{created.Id}",
                created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }
}