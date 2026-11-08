using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace RentRoad.Models;

public class ApplicationUser : IdentityUser
{
    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Location { get; set; }

    public ICollection<Listing> Listings { get; set; } = new List<Listing>();

    public ICollection<RentalRequest> RentalRequests { get; set; } = new List<RentalRequest>();

    public ICollection<Conversation> RentedConversations { get; set; } = new List<Conversation>();

    public ICollection<Message> Messages { get; set; } = new List<Message>();

    public ICollection<UserActivity> Activities { get; set; } = new List<UserActivity>();
}
