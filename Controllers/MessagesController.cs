using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RentRoad.Hubs;
using RentRoad.Services.Messaging;
using RentRoad.ViewModels.Messaging;

namespace RentRoad.Controllers;

/// <summary>Connects authenticated messaging HTTP requests to existing persistence and post-commit delivery without database queries.</summary>
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

    /// <summary>Initializes MVC with existing messaging, independent live delivery and the application's anti-forgery header convention.</summary>
    public MessagesController(IMessagingService messaging, IMessageRealtimeNotifier notifier, IOptions<AntiforgeryOptions> antiforgery)
    {
        this.messaging = messaging;
        this.notifier = notifier;
        antiforgeryHeaderName = antiforgery.Value.HeaderName;
    }

    /// <summary>Prepares the future inbox View from the current account's bounded, service-ordered conversation page.</summary>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = ConversationInboxParameters.DEFAULT_PAGE_SIZE,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return InvalidBinding();
        var result = await messaging.GetInboxAsync(User, new(pageNumber, pageSize), cancellationToken);
        if (!result.IsSuccess) return PageFailure(result);
        ViewData["Title"] = SharedResource.Get("Conversations.Navigation");
        return View("Index", new MessagingInboxViewModel(result.Value!));
    }

    /// <summary>Prepares authorized context and initial bounded history without marking any message as read.</summary>
    [HttpGet("{conversationId:int}")]
    public async Task<IActionResult> ConversationAsync([FromRoute] int conversationId, CancellationToken cancellationToken = default,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = ConversationInboxParameters.DEFAULT_PAGE_SIZE)
    {
        if (!ModelState.IsValid) return InvalidBinding();
        var context = await messaging.GetConversationAsync(conversationId, User, cancellationToken);
        if (!context.IsSuccess) return PageFailure(context);
        var history = await messaging.GetHistoryAsync(conversationId, User, cancellationToken: cancellationToken);
        if (!history.IsSuccess) return PageFailure(history);
        var inbox = await messaging.GetInboxAsync(User, new(pageNumber, pageSize), cancellationToken);
        if (!inbox.IsSuccess) return PageFailure(inbox);
        var endpoints = new MessagingEndpointsViewModel(Url.Action("History", new { conversationId })!, Url.Action("Send", new { conversationId })!,
            Url.Action("Read", new { conversationId })!, Url.Action("UnreadCount", new { conversationId })!);
        var model = new ConversationViewModel(new(context.Value!), MessageHistoryViewModel.FromResult(history.Value!),
            new(Url.Content("~" + MessagesHub.ROUTE)), endpoints, antiforgeryHeaderName) { Inbox = new(inbox.Value!) };
        ViewData["Title"] = SharedResource.Get("Conversations.ConversationTitle");
        return View("Conversation", model);
    }

    /// <summary>Creates or resumes only the current renter's listing conversation, then redirects using Post/Redirect/Get.</summary>
    [HttpPost("start")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [ValidateAntiForgeryToken]
    [MessagingJson]
    public async Task<IActionResult> StartAsync([FromForm(Name = "")] ConversationStartInputViewModel input, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return InvalidBinding();
        var result = await messaging.GetOrCreateForListingAsync(input.ListingId, User, cancellationToken);
        return result.IsSuccess ? RedirectToAction("Conversation", new { conversationId = result.Value!.ConversationId })
            : JsonResponse(result, details => new ConversationContextViewModel(details));
    }

    /// <summary>Returns a bounded older/newer persisted message page for authorized display and reconnect recovery.</summary>
    [HttpGet("{conversationId:int}/history")]
    [MessagingJson]
    public async Task<IActionResult> HistoryAsync([FromRoute] int conversationId, [FromQuery] int limit = MessageHistoryParameters.DEFAULT_LIMIT,
        [FromQuery] int? beforeMessageId = null, [FromQuery] int? afterMessageId = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return InvalidBinding();
        var result = await messaging.GetHistoryAsync(conversationId, User, new(limit, beforeMessageId, afterMessageId), cancellationToken);
        return JsonResponse(result, MessageHistoryViewModel.FromResult);
    }

    /// <summary>Persists exact client text/token first, then notifies with that trusted result while preserving saved success if live delivery is unavailable.</summary>
    [HttpPost("{conversationId:int}/send")]
    [ValidateAntiForgeryToken]
    [MessagingJson]
    public async Task<IActionResult> SendAsync([FromRoute] int conversationId, [FromBody] MessageSendInputViewModel? input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return InvalidBinding();
        var result = await messaging.SendAsync(conversationId, User, input?.ToSubmission(), cancellationToken);
        var notification = result.IsSuccess ? await notifier.NotifyMessageSavedAsync(User, result, cancellationToken) : (MessageRealtimeNotificationStatus?)null;
        return JsonResponse(result, MessageViewModel.FromResult, notification);
    }

    /// <summary>Acknowledges only the current participant's received messages through the supplied observed boundary.</summary>
    [HttpPost("{conversationId:int}/read")]
    [ValidateAntiForgeryToken]
    [MessagingJson]
    public async Task<IActionResult> ReadAsync([FromRoute] int conversationId, [FromBody] MessageReadInputViewModel? input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return InvalidBinding();
        var result = await messaging.MarkReadThroughAsync(conversationId, User, input?.ThroughMessageId ?? 0, cancellationToken);
        return JsonResponse(result, read => new MessageReadViewModel(read.ConversationId, read.ThroughMessageId, read.NewlyReadCount));
    }

    /// <summary>Returns the authoritative aggregate or participant-authorized conversation unread count without acknowledging anything.</summary>
    [HttpGet("unread-count")]
    [MessagingJson]
    public async Task<IActionResult> UnreadCountAsync([FromQuery] int? conversationId = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return InvalidBinding();
        var result = await messaging.GetUnreadCountAsync(User, conversationId, cancellationToken);
        return JsonResponse(result, count => new MessagingUnreadCountViewModel(conversationId, count));
    }

    /// <summary>Preserves browser login challenges for page actions while using safe structured failures for other page-load outcomes.</summary>
    private IActionResult PageFailure<T>(MessagingResult<T> result) => result.Status == MessagingServiceStatus.Unauthenticated ? Challenge()
        : JsonResponse(result, _ => (object?)null);

    /// <summary>Maps only confirmed projections and structured errors into a consistent JSON envelope and HTTP status.</summary>
    private JsonResult JsonResponse<TSource, TView>(MessagingResult<TSource> result, Func<TSource, TView> map,
        MessageRealtimeNotificationStatus? notification = null) => new(MessagingResponseViewModel<TView>.FromResult(result, map, notification))
        { StatusCode = StatusCodeFor(result.Status) };

    /// <summary>Returns sanitized field errors for malformed binding without echoing submitted values or framework exception details.</summary>
    private JsonResult InvalidBinding()
    {
        var errors = ModelState.Where(entry => entry.Value?.Errors.Count > 0).Select(entry => new MessagingErrorViewModel(
            BindingField(entry.Key), "Validation.NonPropertyUnknownInvalid", SharedResource.Get("Validation.NonPropertyUnknownInvalid")))
            .DistinctBy(error => error.Field).ToArray();
        return new JsonResult(new MessagingResponseViewModel<object>(false, MessagingServiceStatus.ValidationFailed.ToString(), null, false, false, errors))
            { StatusCode = StatusCodes.Status400BadRequest };
    }

    /// <summary>Returns only known messaging field names, excluding arbitrary JSON paths and attempted values from binding errors.</summary>
    private static string BindingField(string key)
    {
        var field = key.Split('.').Last();
        return new[] { "Content", "ClientMessageId", "ListingId", "ThroughMessageId", "Limit", "BeforeMessageId", "AfterMessageId", "PageNumber", "PageSize", "ConversationId" }
            .FirstOrDefault(known => string.Equals(known, field, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    /// <summary>Preserves private missing equivalence and distinguishes invalid input, conflicts and retryable persistence failures.</summary>
    private static int StatusCodeFor(MessagingServiceStatus status) => status switch
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
