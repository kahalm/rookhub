using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class RequestLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    [MaxLength(10)]
    public string Method { get; set; } = string.Empty;
    [MaxLength(500)]
    public string Path { get; set; } = string.Empty;
    [MaxLength(1000)]
    public string? QueryString { get; set; }
    [MaxLength(100)]
    public string? UserName { get; set; }
    public int? UserId { get; set; }
    [MaxLength(45)]
    public string? IpAddress { get; set; }
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
}
