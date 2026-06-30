using System.Net;
using System.Text;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Infrastructure.Storage;

namespace TemplateProcessor.Tests.Infrastructure
{
    public class MinioBlobStorageTests
    {
        [Fact]
        public async Task CheckWriteAsync_SendsSignedPutRequestToConfiguredBucket()
        {
            HttpRequestMessage? capturedRequest = null;
            var handler = new StubHttpMessageHandler(request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var httpClient = new HttpClient(handler);
            var storage = new MinioBlobStorage(CreateOptions(), httpClient: httpClient);

            await storage.CheckWriteAsync();

            Assert.NotNull(capturedRequest);
            Assert.Equal(HttpMethod.Put, capturedRequest!.Method);
            Assert.Equal(
                "http://localhost:9000/documents/_checks/template-processor-write-check.txt",
                capturedRequest.RequestUri!.ToString());
            Assert.Equal("localhost:9000", capturedRequest.Headers.Host);
            Assert.True(capturedRequest.Headers.Contains("x-amz-date"));
            Assert.True(capturedRequest.Headers.Contains("x-amz-content-sha256"));
            Assert.Equal("AWS4-HMAC-SHA256", capturedRequest.Headers.Authorization!.Scheme);
            Assert.Contains("Credential=minioadmin/", capturedRequest.Headers.Authorization.Parameter);
            Assert.Contains("SignedHeaders=content-type;host;x-amz-content-sha256;x-amz-date", capturedRequest.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task UploadAsync_WhenMinioReturnsError_ThrowsBlobStorageException()
        {
            var handler = new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("Access denied")
                });
            using var httpClient = new HttpClient(handler);
            var storage = new MinioBlobStorage(CreateOptions(), httpClient: httpClient);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("document"));

            await Assert.ThrowsAsync<BlobStorageException>(
                () => storage.UploadAsync("generated/document.docx", content, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
        }

        [Fact]
        public async Task UploadAsync_WhenStreamPositionIsNotZero_UploadsFullContent()
        {
            string? uploadedContent = null;
            var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
            {
                uploadedContent = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var httpClient = new HttpClient(handler);
            var storage = new MinioBlobStorage(CreateOptions(), httpClient: httpClient);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("document"));
            content.Position = content.Length;

            await storage.UploadAsync("generated/document.docx", content, "application/octet-stream");

            Assert.Equal("document", uploadedContent);
        }

        [Theory]
        [InlineData("")]
        [InlineData("/absolute-key")]
        [InlineData("folder\\file.docx")]
        [InlineData("../file.docx")]
        public async Task UploadAsync_WhenObjectKeyIsInvalid_ThrowsArgumentException(string objectKey)
        {
            using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
            var storage = new MinioBlobStorage(CreateOptions(), httpClient: httpClient);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("document"));

            await Assert.ThrowsAsync<ArgumentException>(
                () => storage.UploadAsync(objectKey, content, "application/octet-stream"));
        }

        private static MinioBlobStorageOptions CreateOptions()
        {
            return new MinioBlobStorageOptions
            {
                Endpoint = "http://localhost:9000",
                BucketName = "documents",
                AccessKey = "minioadmin",
                SecretKey = "minioadmin"
            };
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
                : this((request, _) => Task.FromResult(handler(request)))
            {
            }

            public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return _handler(request, cancellationToken);
            }
        }
    }
}
