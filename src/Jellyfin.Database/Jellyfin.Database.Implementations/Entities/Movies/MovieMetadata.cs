namespace MulletaFlix.Database.Implementations.Entities.Movies;

public class MovieMetadata
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public string? Title { get; set; }
    public string? Language { get; set; }
    public bool IsDefault { get; set; }
    public Movie Movie { get; set; } = null!;
}
