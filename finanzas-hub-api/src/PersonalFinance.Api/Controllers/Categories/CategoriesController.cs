using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Categories.Application;
using PersonalFinance.Modules.Categories.Application.Categories;
using PersonalFinance.Modules.Categories.Application.Defaults;
using PersonalFinance.Modules.Categories.Application.Tags;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.Api.Controllers.Categories;

public sealed class CreateCategoryRequest
{
    /// <summary>Required for a category. A subcategory takes its parent's.</summary>
    public CategoryKind? Kind { get; init; }

    [Required, StringLength(Category.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>The category this one goes under. Leave it out to create a category of the first level.</summary>
    public Guid? ParentId { get; init; }

    [StringLength(Category.MaxIconLength)]
    public string? Icon { get; init; }

    public CategoryColor? Color { get; init; }

    [MaxLength(Category.MaxTags)]
    public IReadOnlyList<string>? Tags { get; init; }
}

public sealed class UpdateCategoryRequest
{
    [Required, StringLength(Category.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(Category.MaxIconLength)]
    public string? Icon { get; init; }

    public CategoryColor? Color { get; init; }
}

public sealed class SetCategoryTagsRequest
{
    [Required, MaxLength(Category.MaxTags)]
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed class MoveCategoryRequest
{
    /// <summary>The category of the first level the subcategory goes under. It must be of the same kind.</summary>
    [Required]
    public Guid? ParentId { get; init; }
}

public sealed class RenameTagRequest
{
    [Required, StringLength(Tag.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>The categories of the signed-in person in this workspace. Nobody else's are reachable from here.</summary>
[Tags("Categories")]
[Route("api/v1/workspaces/{workspaceId:guid}/categories")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class CategoriesController : ApiControllerBase
{
    [HttpGet(Name = "ListCategories")]
    [ProducesResponseType<IReadOnlyList<CategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromQuery] CategoryKind? kind, [FromQuery] bool includeArchived,
        [FromServices] IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListCategoriesQuery(kind, includeArchived), cancellationToken), Ok);

    /// <summary>Creates a category or, with a parent, a subcategory. Answers with the whole category either way.</summary>
    [HttpPost(Name = "CreateCategory")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        Guid workspaceId, CreateCategoryRequest request, [FromServices] ICommandHandler<CreateCategoryCommand, CategoryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new CreateCategoryCommand(request.Kind, request.Name, request.ParentId, request.Icon, request.Color, request.Tags), cancellationToken),
            category => Created($"/api/v1/workspaces/{workspaceId}/categories", category));

    [HttpPatch("{categoryId:guid}", Name = "UpdateCategory")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid workspaceId, Guid categoryId, UpdateCategoryRequest request,
        [FromServices] ICommandHandler<UpdateCategoryCommand, CategoryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new UpdateCategoryCommand(categoryId, request.Name, request.Icon, request.Color), cancellationToken), Ok);

    /// <summary>Moves a subcategory under another category. Answers with the category it moved to.</summary>
    [HttpPost("{categoryId:guid}/move", Name = "MoveCategory")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Move(
        Guid workspaceId, Guid categoryId, MoveCategoryRequest request,
        [FromServices] ICommandHandler<MoveCategoryCommand, CategoryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new MoveCategoryCommand(categoryId, request.ParentId!.Value), cancellationToken), Ok);

    /// <summary>Archiving a category archives its subcategories. Nothing is deleted.</summary>
    [HttpPost("{categoryId:guid}/archive", Name = "ArchiveCategory")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(
        Guid workspaceId, Guid categoryId, [FromServices] ICommandHandler<ArchiveCategoryCommand, CategoryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ArchiveCategoryCommand(categoryId), cancellationToken), Ok);

    [HttpPost("{categoryId:guid}/restore", Name = "RestoreCategory")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Restore(
        Guid workspaceId, Guid categoryId, [FromServices] ICommandHandler<RestoreCategoryCommand, CategoryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RestoreCategoryCommand(categoryId), cancellationToken), Ok);

    /// <summary>Replaces the tags the category suggests. A name that is not a tag yet becomes one.</summary>
    [HttpPut("{categoryId:guid}/tags", Name = "SetCategoryTags")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetTags(
        Guid workspaceId, Guid categoryId, SetCategoryTagsRequest request,
        [FromServices] ICommandHandler<SetCategoryTagsCommand, CategoryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new SetCategoryTagsCommand(categoryId, request.Tags), cancellationToken), Ok);

    /// <summary>Adds the default categories the person does not have yet. Safe to repeat.</summary>
    [HttpPost("defaults", Name = "SeedDefaultCategories")]
    [ProducesResponseType<SeededCategoriesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SeedDefaults(
        Guid workspaceId, [FromServices] ICommandHandler<SeedDefaultCategoriesCommand, SeededCategoriesResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new SeedDefaultCategoriesCommand(), cancellationToken), Ok);
}

/// <summary>The tags of the signed-in person in this workspace.</summary>
[Tags("Categories")]
[Route("api/v1/workspaces/{workspaceId:guid}/tags")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class TagsController : ApiControllerBase
{
    [HttpGet(Name = "ListTags")]
    [ProducesResponseType<IReadOnlyList<TagResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid workspaceId, [FromServices] IQueryHandler<ListTagsQuery, IReadOnlyList<TagResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListTagsQuery(), cancellationToken), Ok);

    /// <summary>Renames the tag everywhere it is used: categories and transactions point to it, not to its name.</summary>
    [HttpPatch("{tagId:guid}", Name = "RenameTag")]
    [ProducesResponseType<TagResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(
        Guid workspaceId, Guid tagId, RenameTagRequest request, [FromServices] ICommandHandler<RenameTagCommand, TagResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RenameTagCommand(tagId, request.Name), cancellationToken), Ok);

    /// <summary>Archives the tag and removes it from every category that suggested it.</summary>
    [HttpPost("{tagId:guid}/archive", Name = "ArchiveTag")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid workspaceId, Guid tagId, [FromServices] ICommandHandler<ArchiveTagCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ArchiveTagCommand(tagId), cancellationToken), _ => NoContent());
}
