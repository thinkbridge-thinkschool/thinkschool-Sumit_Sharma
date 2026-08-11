using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Time;

namespace QuotesApi.Extensions;

public static class CollectionEndpointExtensions
{
    public static void MapCollectionEndpoints(
        this WebApplication app)
    {
        app.MapPost("/api/collections", async (
            CollectionRequest request,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var collection = new Collection(
                    request.Name,
                    request.OwnerId);

                var created = await repository.AddAsync(
                    collection,
                    cancellationToken);

                return Results.Created(
                    $"/api/collections/{created.Id}",
                    created);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Collection validation failed",
                    detail: ex.Message);
            }
        });

        app.MapPost(
            "/api/collections/{id:int}/quotes/{quoteId:int}",
            async (
                int id,
                int quoteId,
                ICollectionRepository collectionRepository,
                IQuoteRepository quoteRepository,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                var collection =
                    await collectionRepository.GetByIdAsync(
                        id,
                        cancellationToken);

                if (collection is null)
                    return Results.NotFound();

                var quote =
                    await quoteRepository.GetByIdAsync(
                        quoteId,
                        cancellationToken);

                if (quote is null)
                    return Results.NotFound();

                try
                {
                    collection.AddItem(
                        quoteId,
                        clock);

                    await collectionRepository.UpdateAsync(
                        collection,
                        cancellationToken);

                    return Results.Ok(collection);
                }
                catch (ArgumentException ex)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Collection validation failed",
                        detail: ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Collection invariant violated",
                        detail: ex.Message);
                }
            });

        app.MapDelete(
            "/api/collections/{id:int}/quotes/{quoteId:int}",
            async (
                int id,
                int quoteId,
                ICollectionRepository repository,
                CancellationToken cancellationToken) =>
            {
                var collection =
                    await repository.GetByIdAsync(
                        id,
                        cancellationToken);

                if (collection is null)
                    return Results.NotFound();

                try
                {
                    collection.RemoveItem(quoteId);

                    await repository.UpdateAsync(
                        collection,
                        cancellationToken);

                    return Results.Ok(collection);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Collection invariant violated",
                        detail: ex.Message);
                }
            });

        app.MapGet(
            "/api/collections/{id:int}",
            async (
                int id,
                ICollectionRepository repository,
                CancellationToken cancellationToken) =>
            {
                var collection =
                    await repository.GetByIdAsync(
                        id,
                        cancellationToken);

                return collection is null
                    ? Results.NotFound()
                    : Results.Ok(collection);
            });

        app.MapDelete(
            "/api/collections/{id:int}",
            async (
                int id,
                ICollectionRepository repository,
                CancellationToken cancellationToken) =>
            {
                var deleted =
                    await repository.DeleteAsync(
                        id,
                        cancellationToken);

                return deleted
                    ? Results.NoContent()
                    : Results.NotFound();
            });
    }

    public sealed record CollectionRequest(
        string Name,
        int OwnerId);
}
