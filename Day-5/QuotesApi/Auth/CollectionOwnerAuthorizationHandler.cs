using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Auth;

public class CollectionOwnerAuthorizationHandler
    : AuthorizationHandler<CollectionOwnerRequirement, Collection>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CollectionOwnerRequirement requirement,
        Collection resource)
    {
        var userId = context.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (userId is not null &&
            int.TryParse(userId, out var parsedUserId) &&
            parsedUserId == resource.OwnerId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
