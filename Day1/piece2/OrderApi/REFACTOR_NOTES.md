# Refactor Notes

1. The controller is doing too much work in one method.
2. Business logic is mixed with HTTP handling.
3. Database/storage logic is inside the controller.
4. The code uses `Thread.Sleep()` inside an async method.
5. There are multiple empty `catch` blocks that hide errors.
6. Exceptions are handled with `Console.WriteLine()` instead of proper logging.
7. The method returns `object` instead of clear HTTP responses.
8. There is no proper validation model for the incoming order.
9. The `i <= order.Items.Count` loop has an off-by-one bug.
10. `item.ProductName` can be null and cause a null reference exception.
11. `order.Customer.Address.City` can also cause a null reference exception.
12. The controller contains pricing and discount business rules.
13. The controller directly manages the order collection.
14. The code is difficult to test because everything is inside one action.
15. There is no separation between controller, service and repository responsibilities.
16. `Thread.Sleep()` blocks the request thread.
17. The code uses a fake `Task.Delay()` instead of doing real asynchronous work.
18. Error messages are inconsistent and are returned as plain strings.
19. The static list is not a proper data persistence layer.
20. The controller has too many responsibilities and violates single responsibility.