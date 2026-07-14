using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.MediaEncoding;

public interface IStrmPrebufferManager
{
    Task PrepareAsync(BaseItem item);

    bool TryGetProxyUrl(Guid itemId, out string url);

    string? GetContentType(Guid itemId);

    Task<(string ContentType, long? ContentLength)> CopyToAsync(Guid itemId, Stream output, CancellationToken cancellationToken);
}
