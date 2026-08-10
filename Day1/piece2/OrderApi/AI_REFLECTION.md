# AI Reflection

For this refactor, I focused on separating the pricing rules from OrderService. The main problem was that OrderService was responsible for validation, pricing calculations, logging, and repository access. I moved the pricing calculation into IOrderPricingStrategy and OrderPricingStrategy so the service is easier to maintain and new pricing rules can be added without changing the main order workflow.

The AI-assisted approach was useful for thinking about the design, especially identifying the pricing logic as a separate responsibility. However, I still needed to review the proposed approach because it would be easy to over-engineer a small requirement by creating too many abstractions or classes. I kept the implementation intentionally small with one interface and one concrete strategy.

For testing, I added cases for negative quantities, missing customer names, and missing customer emails. These tests are useful because they verify the validation rules rather than only checking the successful order path. During the refactor, the tests initially caught a constructor mismatch because the service gained a new pricing-strategy dependency. Fixing that showed why running tests after each structural change is important.

If I were debugging this at 2 AM, I would use Claude for understanding a larger unfamiliar codebase and discussing design alternatives, while I would use Copilot for smaller repetitive tasks such as generating straightforward tests. In both cases, I would review the generated code before accepting it because the final responsibility for correctness is still mine.

The final test suite contains seven passing tests: six unit tests and one integration test.
