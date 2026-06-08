using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MulletaFlix.Server.Integration.Tests
{
    public sealed class WebSocketTests : IClassFixture<MulletaFlixApplicationFactory>
    {
        private readonly MulletaFlixApplicationFactory _factory;

        public WebSocketTests(MulletaFlixApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task WebSocket_Unauthenticated_ThrowsInvalidOperationException()
        {
            var server = _factory.Server;
            var client = server.CreateWebSocketClient();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ConnectAsync(
                    new UriBuilder(server.BaseAddress)
                    {
                        Scheme = "ws",
                        Path = "websocket"
                    }.Uri,
                    CancellationToken.None));
        }
    }
}

