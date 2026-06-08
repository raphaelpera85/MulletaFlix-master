using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;
using Xunit;

namespace MulletaFlix.Server.Integration.Tests.Controllers
{
    public sealed class ActivityLogControllerTests : IClassFixture<MulletaFlixApplicationFactory>
    {
        private readonly MulletaFlixApplicationFactory _factory;
        private static string? _accessToken;

        public ActivityLogControllerTests(MulletaFlixApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task ActivityLog_GetEntries_Ok()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

            var response = await client.GetAsync("System/ActivityLog/Entries", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);
        }
    }
}

