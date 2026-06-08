using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Api.Extensions;
using MulletaFlix.Api.Helpers;
using MulletaFlix.Api.ModelBinders;
using MulletaFlix.Data.Enums;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Extensions;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MulletaFlix.Api.Controllers;

/// <summary>
/// User library controller.
/// </summary>
[Route("")]
[Authorize]
[Tags("Library")]
public class UserLibraryController : BaseMulletaFlixApiController
{
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataRepository;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly IUserViewManager _userViewManager;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserLibraryController"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="userDataRepository">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="userViewManager">Instance of the <see cref="IUserViewManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    public UserLibraryController(
        IUserManager userManager,
        IUserDataManager userDataRepository,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IUserViewManager userViewManager,
        IFileSystem fileSystem)
    {
        _userManager = userManager;
        _userDataRepository = userDataRepository;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _userViewManager = userViewManager;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Gets an item from a user's library.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Item returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the item.</returns>
    [HttpGet("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseItemDto>> GetItem(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        await RefreshItemOnDemandIfNeeded(item).ConfigureAwait(false);

        var dtoOptions = new DtoOptions();

        return await _dtoService.GetBaseItemDtoAsync(item, dtoOptions, user).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets an item from a user's library.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Item returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the item.</returns>
    [HttpGet("Users/{userId}/Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<ActionResult<BaseItemDto>> GetItemLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId)
        => GetItem(userId, itemId);

    /// <summary>
    /// Gets the root folder from a user's library.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <response code="200">Root folder returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the user's root folder.</returns>
    [HttpGet("Items/Root")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseItemDto>> GetRootFolder([FromQuery] Guid? userId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = _libraryManager.GetUserRootFolder();
        var dtoOptions = new DtoOptions();
        return await _dtoService.GetBaseItemDtoAsync(item, dtoOptions, user).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the root folder from a user's library.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <response code="200">Root folder returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the user's root folder.</returns>
    [HttpGet("Users/{userId}/Items/Root")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<BaseItemDto>> GetRootFolderLegacy(
        [FromRoute, Required] Guid userId)
        => await GetRootFolder(userId).ConfigureAwait(false);

    /// <summary>
    /// Gets intros to play before the main media item plays.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Intros returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the intros to play.</returns>
    [HttpGet("Items/{itemId}/Intros")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<QueryResult<BaseItemDto>>> GetIntros(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        var items = await _libraryManager.GetIntros(item, user).ConfigureAwait(false);
        var dtoOptions = new DtoOptions();
        var dtos = await _dtoService.GetBaseItemDtosAsync(items.ToList(), dtoOptions, user).ConfigureAwait(false);

        return new QueryResult<BaseItemDto>(dtos);
    }

    /// <summary>
    /// Gets intros to play before the main media item plays.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Intros returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the intros to play.</returns>
    [HttpGet("Users/{userId}/Items/{itemId}/Intros")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<ActionResult<QueryResult<BaseItemDto>>> GetIntrosLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId)
        => GetIntros(userId, itemId);

    /// <summary>
    /// Marks an item as a favorite.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Item marked as favorite.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpPost("UserFavoriteItems/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Tags("UserData")]
    public ActionResult<UserItemDataDto> MarkFavoriteItem(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        return MarkFavorite(user, item, true);
    }

    /// <summary>
    /// Marks an item as a favorite.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Item marked as favorite.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpPost("Users/{userId}/FavoriteItems/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult<UserItemDataDto> MarkFavoriteItemLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId)
        => MarkFavoriteItem(userId, itemId);

    /// <summary>
    /// Unmarks item as a favorite.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Item unmarked as favorite.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpDelete("UserFavoriteItems/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Tags("UserData")]
    public ActionResult<UserItemDataDto> UnmarkFavoriteItem(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        return MarkFavorite(user, item, false);
    }

    /// <summary>
    /// Unmarks item as a favorite.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Item unmarked as favorite.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpDelete("Users/{userId}/FavoriteItems/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult<UserItemDataDto> UnmarkFavoriteItemLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId)
        => UnmarkFavoriteItem(userId, itemId);

    /// <summary>
    /// Deletes a user's saved personal rating for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Personal rating removed.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpDelete("UserItems/{itemId}/Rating")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Tags("UserData")]
    public ActionResult<UserItemDataDto?> DeleteUserItemRating(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        return UpdateUserItemRatingInternal(user, item, null);
    }

    /// <summary>
    /// Deletes a user's saved personal rating for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Personal rating removed.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpDelete("Users/{userId}/Items/{itemId}/Rating")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult<UserItemDataDto?> DeleteUserItemRatingLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId)
        => DeleteUserItemRating(userId, itemId);

    /// <summary>
    /// Updates a user's rating for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="likes">Whether this <see cref="UpdateUserItemRating" /> is likes.</param>
    /// <response code="200">Item rating updated.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpPost("UserItems/{itemId}/Rating")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Tags("UserData")]
    public ActionResult<UserItemDataDto?> UpdateUserItemRating(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId,
        [FromQuery] bool? likes)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        return UpdateUserItemRatingInternal(user, item, likes);
    }

    /// <summary>
    /// Updates a user's rating for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="likes">Whether this <see cref="UpdateUserItemRating" /> is likes.</param>
    /// <response code="200">Item rating updated.</response>
    /// <returns>An <see cref="OkResult"/> containing the <see cref="UserItemDataDto"/>.</returns>
    [HttpPost("Users/{userId}/Items/{itemId}/Rating")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult<UserItemDataDto?> UpdateUserItemRatingLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId,
        [FromQuery] bool? likes)
        => UpdateUserItemRating(userId, itemId, likes);

    /// <summary>
    /// Gets local trailers for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">An <see cref="OkResult"/> containing the item's local trailers.</response>
    /// <returns>The items local trailers.</returns>
    [HttpGet("Items/{itemId}/LocalTrailers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BaseItemDto>>> GetLocalTrailers(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        var dtoOptions = new DtoOptions();
        if (item is IHasTrailers hasTrailers)
        {
            var trailers = hasTrailers.LocalTrailers;
            var dtos = await _dtoService.GetBaseItemDtosAsync(trailers, dtoOptions, user, item).ConfigureAwait(false);
            return Ok(dtos.AsEnumerable());
        }

        var extras = item.GetExtras().Where(e => e.ExtraType == ExtraType.Trailer).ToList();
        var extraDtos = await _dtoService.GetBaseItemDtosAsync(extras, dtoOptions, user, item).ConfigureAwait(false);
        return Ok(extraDtos.AsEnumerable());
    }

    /// <summary>
    /// Gets local trailers for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">An <see cref="OkResult"/> containing the item's local trailers.</response>
    /// <returns>The items local trailers.</returns>
    [HttpGet("Users/{userId}/Items/{itemId}/LocalTrailers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<IEnumerable<BaseItemDto>>> GetLocalTrailersLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId)
        => await GetLocalTrailers(userId, itemId).ConfigureAwait(false);

    /// <summary>
    /// Gets special features for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Special features returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the special features.</returns>
    [HttpGet("Items/{itemId}/SpecialFeatures")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BaseItemDto>>> GetSpecialFeatures(
        [FromQuery] Guid? userId,
        [FromRoute, Required] Guid itemId)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(userId.Value);
        if (user is null)
        {
            return NotFound();
        }

        var item = itemId.IsEmpty()
            ? _libraryManager.GetUserRootFolder()
            : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        var dtoOptions = new DtoOptions();
        var extras = item.GetExtras()
            .Where(i => i.ExtraType.HasValue && BaseItem.DisplayExtraTypes.Contains(i.ExtraType.Value))
            .ToList();
        var dtos = await _dtoService.GetBaseItemDtosAsync(extras, dtoOptions, user, item).ConfigureAwait(false);

        return Ok(dtos.AsEnumerable());
    }

    /// <summary>
    /// Gets special features for an item.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Special features returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the special features.</returns>
    [HttpGet("Users/{userId}/Items/{itemId}/SpecialFeatures")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<IEnumerable<BaseItemDto>>> GetSpecialFeaturesLegacy(
        [FromRoute, Required] Guid userId,
        [FromRoute, Required] Guid itemId)
        => await GetSpecialFeatures(userId, itemId).ConfigureAwait(false);

    /// <summary>
    /// Gets latest media.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="parentId">Specify this to localize the search to a specific item or folder. Omit to use the root.</param>
    /// <param name="fields">Optional. Specify additional fields of information to return in the output.</param>
    /// <param name="includeItemTypes">Optional. If specified, results will be filtered based on item type. This allows multiple, comma delimited.</param>
    /// <param name="isPlayed">Filter by items that are played, or not.</param>
    /// <param name="enableImages">Optional. include image information in output.</param>
    /// <param name="imageTypeLimit">Optional. the max number of images to return, per image type.</param>
    /// <param name="enableImageTypes">Optional. The image types to include in the output.</param>
    /// <param name="enableUserData">Optional. include user data.</param>
    /// <param name="limit">Return item limit.</param>
    /// <param name="groupItems">Whether or not to group items into a parent container.</param>
    /// <response code="200">Latest media returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the latest media.</returns>
    [HttpGet("Items/Latest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BaseItemDto>>> GetLatestMedia(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? parentId,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] ItemFields[] fields,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] BaseItemKind[] includeItemTypes,
        [FromQuery] bool? isPlayed,
        [FromQuery] bool? enableImages,
        [FromQuery] int? imageTypeLimit,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] ImageType[] enableImageTypes,
        [FromQuery] bool? enableUserData,
        [FromQuery] int limit = 20,
        [FromQuery] bool groupItems = true)
    {
        var requestUserId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(requestUserId);
        if (user is null)
        {
            return NotFound();
        }

        if (!isPlayed.HasValue)
        {
            if (user.HidePlayedInLatest)
            {
                isPlayed = false;
            }
        }

        var dtoOptions = new DtoOptions { Fields = fields }
            .AddAdditionalDtoOptions(enableImages, enableUserData, imageTypeLimit, enableImageTypes);

        dtoOptions.PreferEpisodeParentPoster = true;

        var list = await _userViewManager.GetLatestItemsAsync(
            new LatestItemsQuery
            {
                GroupItems = groupItems,
                IncludeItemTypes = includeItemTypes,
                IsPlayed = isPlayed,
                Limit = limit,
                ParentId = parentId ?? Guid.Empty,
                User = user,
            },
            dtoOptions).ConfigureAwait(false);

        var resolvedItems = new BaseItem[list.Count];
        var childCounts = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            var tuple = list[i];
            var item = tuple.Item2[0];
            var childCount = 0;

            if (tuple.Item1 is not null && (tuple.Item2.Count > 1 || tuple.Item1 is MusicAlbum))
            {
                item = tuple.Item1;
                childCount = tuple.Item2.Count;
            }

            resolvedItems[i] = item;
            childCounts[i] = childCount;
        }

        // Fetch DTOs without visibility check since we've already done that in GetLatestItems and restore child counts afterwards
        var dtos = await _dtoService.GetBaseItemDtosAsync(resolvedItems, dtoOptions, user, skipVisibilityCheck: true).ConfigureAwait(false);
        for (int i = 0; i < dtos.Count; i++)
        {
            if (childCounts[i] > 0)
            {
                dtos[i].ChildCount = childCounts[i];
            }
        }

        return Ok(dtos.AsEnumerable());
    }

    /// <summary>
    /// Gets latest media.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="parentId">Specify this to localize the search to a specific item or folder. Omit to use the root.</param>
    /// <param name="fields">Optional. Specify additional fields of information to return in the output.</param>
    /// <param name="includeItemTypes">Optional. If specified, results will be filtered based on item type. This allows multiple, comma delimited.</param>
    /// <param name="isPlayed">Filter by items that are played, or not.</param>
    /// <param name="enableImages">Optional. include image information in output.</param>
    /// <param name="imageTypeLimit">Optional. the max number of images to return, per image type.</param>
    /// <param name="enableImageTypes">Optional. The image types to include in the output.</param>
    /// <param name="enableUserData">Optional. include user data.</param>
    /// <param name="limit">Return item limit.</param>
    /// <param name="groupItems">Whether or not to group items into a parent container.</param>
    /// <response code="200">Latest media returned.</response>
    /// <returns>An <see cref="OkResult"/> containing the latest media.</returns>
    [HttpGet("Users/{userId}/Items/Latest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Kept for backwards compatibility")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<ActionResult<IEnumerable<BaseItemDto>>> GetLatestMediaLegacy(
        [FromRoute, Required] Guid userId,
        [FromQuery] Guid? parentId,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] ItemFields[] fields,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] BaseItemKind[] includeItemTypes,
        [FromQuery] bool? isPlayed,
        [FromQuery] bool? enableImages,
        [FromQuery] int? imageTypeLimit,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] ImageType[] enableImageTypes,
        [FromQuery] bool? enableUserData,
        [FromQuery] int limit = 20,
        [FromQuery] bool groupItems = true)
        => GetLatestMedia(
            userId,
            parentId,
            fields,
            includeItemTypes,
            isPlayed,
            enableImages,
            imageTypeLimit,
            enableImageTypes,
            enableUserData,
            limit,
            groupItems);

    private async Task RefreshItemOnDemandIfNeeded(BaseItem item)
    {
        if (item is Person)
        {
            var hasMetadata = !string.IsNullOrWhiteSpace(item.Overview) && item.HasImage(ImageType.Primary);
            var performFullRefresh = !hasMetadata && (DateTime.UtcNow - item.DateLastRefreshed).TotalDays >= 3;

            if (performFullRefresh)
            {
                var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ForceSave = true
                };

                await item.RefreshMetadata(options, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Marks the favorite.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="item">The item.</param>
    /// <param name="isFavorite">if set to <c>true</c> [is favorite].</param>
    private UserItemDataDto MarkFavorite(User user, BaseItem item, bool isFavorite)
    {
        // Get the user data for this item
        var data = _userDataRepository.GetUserData(user, item);

        if (data is not null)
        {
            // Set favorite status
            data.IsFavorite = isFavorite;

            _userDataRepository.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
        }

        return _userDataRepository.GetUserDataDto(item, user)!;
    }

    /// <summary>
    /// Updates the user item rating.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="item">The item.</param>
    /// <param name="likes">if set to <c>true</c> [likes].</param>
    private UserItemDataDto? UpdateUserItemRatingInternal(User user, BaseItem item, bool? likes)
    {
        // Get the user data for this item
        var data = _userDataRepository.GetUserData(user, item);

        if (data is not null)
        {
            data.Likes = likes;

            _userDataRepository.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
        }

        return _userDataRepository.GetUserDataDto(item, user);
    }
}

