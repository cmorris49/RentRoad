using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RentRoad.Hubs;
using RentRoad.Services.Messaging;
using RentRoad.ViewModels.Messaging;

namespace RentRoad.Controllers;

/// <summary>
/// Connects authenticated messaging HTTP requests to existing persistence and post-commit delivery without
/// database queries.
/// </summary>
[Authorize]
[Route("messages")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(MAX_REQUEST_BYTES)]
public sealed class MessagesController : Controller
{
    private const long MAX_REQUEST_BYTES = 32 * 1024;
    private readonly IMessagingService messaging;
    private readonly IMessageRealtimeNotifier notifier;
    private readonly string? antiforgeryHeaderName;

    /// <summary>
    /// Initializes MVC with existing messaging, independent live delivery and the application's
    /// anti-forgery header convention.
    /// </summary>
    /// <param name="messaging">The service used to authorize and access conversations and messages.</param>
    /// <param name="notifier">The notifier used after confirmed message persistence.</param>
    /// <param name="antiforgery">The configured anti-forgery options for messaging requests.</param>
    public MessagesController(
        IMessagingService messaging,
        IMessageRealtimeNotifier notifier,
        IOptions<AntiforgeryOptions> antiforgery)
    {
        this.messaging = messaging;
        this.notifier = notifier;
        antiforgeryHeaderName = antiforgery.Value.HeaderName;
    }

    /// <summary>
    /// Prepares the future inbox View from the current account's bounded, service-ordered conversation page.
    /// </summary>
    /// <param name="pageNumber">The requested one-based page number.</param>
    /// <param name="pageSize">The requested number of records per page.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = ConversationInboxParameters.DEFAULT_PAGE_SIZE,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBinding();
        }

        var result = await messaging.GetInboxAsync(User, new(pageNumber, pageSize), cancellationToken);

        if (!result.IsSuccess)
        {
            return PageFailure(result);
        }

        ViewData["Title"] = SharedResource.Get("Conversations.Navigation");
        return View("Index", new MessagingInboxViewModel(result.Value!));
    }

    /// <summary>
    /// Prepares authorized context and initial bounded history without marking any message as read.
    /// </summary>
    /// <param name="conversationId">The identifier of the conversation selected by the route.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <param name="pageNumber">The requested one-based page number.</param>
    /// <param name="pageSize">The requested number of records per page.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpGet("{conversationId:int}")]
    public async Task<IActionResult> ConversationAsync(
        [FromRoute] int conversationId,
        CancellationToken cancellationToken = default,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = ConversationInboxParameters.DEFAULT_PAGE_SIZE)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBinding();
        }

        var context = await messaging.GetConversationAsync(conversationId, User, cancellationToken);

        if (!context.IsSuccess)
        {
            return PageFailure(context);
        }

        var history = await messaging.GetHistoryAsync(conversationId, User, cancellationToken: cancellationToken);

        if (!history.IsSuccess)
        {
            return PageFailure(history);
        }

        var inbox = await messaging.GetInboxAsync(User, new(pageNumber, pageSize), cancellationToken);

        if (!inbox.IsSuccess)
        {
            return PageFailure(inbox);
        }

        var endpoints = new MessagingEndpointsViewModel(
            Url.Action("History", new { conversationId })!,
            Url.Action("Send", new { conversationId })!,
            Url.Action("Read", new { conversationId })!,
            Url.Action("UnreadCount", new { conversationId })!);
        var model = new ConversationViewModel(
            new(context.Value!),
            MessageHistoryViewModel.FromResult(history.Value!),
            new(Url.Content("~" + MessagesHub.ROUTE)),
            endpoints,
            antiforgeryHeaderName)
        {
            Inbox = new(inbox.Value!)
        };

        ViewData["Title"] = SharedResource.Get("Conversations.ConversationTitle");
        return View("Conversation", model);
    }

    /// <summary>
    /// Creates or resumes only the current renter's listing conversation, then redirects using Post/Redirect/Get.
    /// </summary>
    /// <param name="input">The listing identifier used to start or resume a conversation.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("start")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [ValidateAntiForgeryToken]
    [MessagingJson]
    public async Task<IActionResult> StartAsync(
        [FromForm(Name = "")] ConversationStartInputViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBinding();
        }

        var result = await messaging.GetOrCreateForListingAsync(input.ListingId, User, cancellationToken);

        return result.IsSuccess
            ? RedirectToAction("Conversation", new { conversationId = result.Value!.ConversationId })
            : JsonResponse(result, details => new ConversationContextViewModel(details));
    }

    /// <summary>
    /// Returns a bounded older/newer persisted message page for authorized display and reconnect recovery.
    /// </summary>
    /// <param name="conversationId">The identifier of the conversation selected by the route.</param>
    /// <param name="limit">The requested maximum number of messages in the history page.</param>
    /// <param name="beforeMessageId">The optional exclusive upper message identifier for older history.</param>
    /// <param name="afterMessageId">The optional exclusive lower message identifier for newer history.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpGet("{conversationId:int}/history")]
    [MessagingJson]
    public async Task<IActionResult> HistoryAsync(
        [FromRoute] int conversationId,
        [FromQuery] int limit = MessageHistoryParameters.DEFAULT_LIMIT,
        [FromQuery] int? beforeMessageId = null,
        [FromQuery] int? afterMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBinding();
        }

        var result = await messaging.GetHistoryAsync(
            conversationId,
            User,
            new(limit, beforeMessageId, afterMessageId),
            cancellationToken);

        return JsonResponse(result, MessageHistoryViewModel.FromResult);
    }

    /// <summary>
    /// Persists exact client text/token first, then notifies with that trusted result while preserving
    /// saved success if live delivery is unavailable.
    /// </summary>
    /// <param name="conversationId">The identifier of the conversation selected by the route.</param>
    /// <param name="input">The submitted message text and retry identifier, when supplied.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("{conversationId:int}/send")]
    [ValidateAntiForgeryToken]
    [MessagingJson]
    public async Task<IActionResult> SendAsync(
        [FromRoute] int conversationId,
        [FromBody] MessageSendInputViewModel? input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBinding();
        }

        var result = await messaging.SendAsync(conversationId, User, input?.ToSubmission(), cancellationToken);
        var notification = result.IsSuccess
            ? await notifier.NotifyMessageSavedAsync(User, result, cancellationToken)
            : (MessageRealtimeNotificationStatus?)null;

        return JsonResponse(result, MessageViewModel.FromResult, notification);
    }

    /// <summary>
    /// Acknowledges only the current participant's received messages through the supplied observed boundary.
    /// </summary>
    /// <param name="conversationId">The identifier of the conversation selected by the route.</param>
    /// <param name="input">The observed message boundary to acknowledge, when supplied.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("{conversationId:int}/read")]
    [ValidateAntiForgeryToken]
    [MessagingJson]
    public async Task<IActionResult> ReadAsync(
        [FromRoute] int conversationId,
        [FromBody] MessageReadInputViewModel? input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBinding();
        }

        var result = await messaging.MarkReadThroughAsync(
            conversationId,
            User,
            input?.ThroughMessageId ?? 0,
            cancellationToken);

        return JsonResponse(
            result,
            read => new MessageReadViewModel(read.ConversationId, read.ThroughMessageId, read.NewlyReadCount));
    }

    /// <summary>
    /// Returns the authoritative aggregate or participant-authorized conversation unread count without
    /// acknowledging anything.
    /// </summary>
    /// <param name="conversationId">The optional conversation identifier supplied by the request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpGet("unread-count")]
    [MessagingJson]
    public async Task<IActionResult> UnreadCountAsync(
        [FromQuery] int? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBinding();
        }

        var result = await messaging.GetUnreadCountAsync(User, conversationId, cancellationToken);

        return JsonResponse(result, count => new MessagingUnreadCountViewModel(conversationId, count));
    }

    /// <summary>
    /// Preserves browser login challenges for page actions while using safe structured failures for other
    /// page-load outcomes.
    /// </summary>
    /// <typeparam name="T">The value type returned by the service operation.</typeparam>
    /// <param name="result">The service result to translate into a response.</param>
    /// <returns>The MVC response for the operation's outcome.</returns>
    private IActionResult PageFailure<T>(MessagingResult<T> result) =>
        result.Status == MessagingServiceStatus.Unauthenticated
            ? Challenge()
            : JsonResponse(result, _ => (object?)null);

    /// <summary>
    /// Maps only confirmed projections and structured errors into a consistent JSON envelope and HTTP status.
    /// </summary>
    /// <typeparam name="TSource">The service result's source value type.</typeparam>
    /// <typeparam name="TView">The response projection type.</typeparam>
    /// <param name="result">The service result to translate into a response.</param>
    /// <param name="map">The function projecting a successful service value into response data.</param>
    /// <param name="notification">The optional realtime notification outcome after saving a message.</param>
    /// <returns>The structured JSON response with the corresponding HTTP status.</returns>
    private JsonResult JsonResponse<TSource, TView>(
        MessagingResult<TSource> result,
        Func<TSource, TView> map,
        MessageRealtimeNotificationStatus? notification = null) =>
        new(MessagingResponseViewModel<TView>.FromResult(result, map, notification))
        {
            StatusCode = StatusCodeFor(result.Status)
        };

    /// <summary>
    /// Returns sanitized field errors for malformed binding without echoing submitted values or framework
    /// exception details.
    /// </summary>
    /// <returns>The structured JSON response with the corresponding HTTP status.</returns>
    private JsonResult InvalidBinding()
    {
        var errors = ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .Select(entry => new MessagingErrorViewModel(
                BindingField(entry.Key),
                "Validation.NonPropertyUnknownInvalid",
                SharedResource.Get("Validation.NonPropertyUnknownInvalid")))
            .DistinctBy(error => error.Field)
            .ToArray();

        return new JsonResult(
            new MessagingResponseViewModel<object>(
                false,
                MessagingServiceStatus.ValidationFailed.ToString(),
                null,
                false,
                false,
                errors))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
    }

    /// <summary>
    /// Returns only known messaging field names, excluding arbitrary JSON paths and attempted values from
    /// binding errors.
    /// </summary>
    /// <param name="key">The model-binding field key to match against known messaging fields.</param>
    /// <returns>The recognized messaging field name, or an empty name for unknown fields.</returns>
    private static string BindingField(string key)
    {
        var field = key.Split('.').Last();

        return new[]
        {
            "Content",
            "ClientMessageId",
            "ListingId",
            "ThroughMessageId",
            "Limit",
            "BeforeMessageId",
            "AfterMessageId",
            "PageNumber",
            "PageSize",
            "ConversationId"
        }.FirstOrDefault(known => string.Equals(known, field, StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
    }

    /// <summary>
    /// Preserves private missing equivalence and distinguishes invalid input, conflicts and retryable
    /// persistence failures.
    /// </summary>
    /// <param name="status">The service outcome to translate into an MVC response.</param>
    /// <returns>The HTTP status code for the service outcome.</returns>
    private static int StatusCodeFor(MessagingServiceStatus status) =>
        status switch
        {
            MessagingServiceStatus.Success => StatusCodes.Status200OK,
            MessagingServiceStatus.Unauthenticated => StatusCodes.Status401Unauthorized,
            MessagingServiceStatus.Forbidden => StatusCodes.Status403Forbidden,
            MessagingServiceStatus.NotFound => StatusCodes.Status404NotFound,
            MessagingServiceStatus.ValidationFailed => StatusCodes.Status400BadRequest,
            MessagingServiceStatus.Conflict or MessagingServiceStatus.ConcurrencyFailed => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };
}
