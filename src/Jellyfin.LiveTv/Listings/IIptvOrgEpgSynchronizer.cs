using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MulletaFlix.LiveTv.Listings
{
    public interface IIptvOrgEpgSynchronizer
    {
        Task SynchronizeAsync(CancellationToken cancellationToken);

        IReadOnlyList<IptvOrgChannelMapping> GetMappings();
    }
}
