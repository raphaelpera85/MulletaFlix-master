using System;
using System.Collections.Generic;

namespace MulletaFlix.Database.Implementations.Entities.Books;

public class Book
{
    public int Id { get; set; }
    public Guid BaseItemId { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Overview { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<BookUserData> UserData { get; set; } = new HashSet<BookUserData>();
}
