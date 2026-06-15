using System;

namespace MulletaFlix.Database.Implementations.Entities.Books;

public class BookUserData
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int BookId { get; set; }
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public Book Book { get; set; } = null!;
}
