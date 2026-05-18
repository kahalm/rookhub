namespace RookHub.Api.Models;

public enum FriendshipStatus
{
    Pending,
    Accepted,
    Declined
}

public class Friendship
{
    public int Id { get; set; }

    public int RequesterId { get; set; }
    public AppUser Requester { get; set; } = null!;

    public int AddresseeId { get; set; }
    public AppUser Addressee { get; set; } = null!;

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
