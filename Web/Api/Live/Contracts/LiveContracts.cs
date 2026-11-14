using System.ComponentModel.DataAnnotations;

namespace Web.Api.Live.Contracts;

public sealed class LiveVersionDto
{
    /// <summary>Opaque text; only "is it different from the last one" means anything.</summary>
    [Required]
    public string Version { get; init; } = "";
}
