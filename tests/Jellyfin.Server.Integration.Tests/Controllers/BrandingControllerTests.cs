using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MulletaFlix.Server.Integration.Tests
{
    public sealed class BrandingControllerTests : IClassFixture<MulletaFlixApplicationFactory>
    {
        private readonly MulletaFlixApplicationFactory _factory;

        public BrandingControllerTests(MulletaFlixApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetConfiguration_ReturnsCorrectResponse()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/Branding/Configuration", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Encoding.UTF8.BodyName, response.Content.Headers.ContentType?.CharSet);

            using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(json.RootElement.TryGetProperty("IntroPath", out _));
            Assert.False(json.RootElement.TryGetProperty("introPath", out _));
        }

        [Theory]
        [InlineData("/Branding/Css")]
        [InlineData("/Branding/Css.css")]
        public async Task GetCss_ReturnsCorrectResponse(string url)
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Encoding.UTF8.BodyName, response.Content.Headers.ContentType?.CharSet);
        }
    }
}
