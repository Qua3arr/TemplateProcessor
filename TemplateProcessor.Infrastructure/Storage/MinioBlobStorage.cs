using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;

namespace TemplateProcessor.Infrastructure.Storage
{
    public class MinioBlobStorage : IBlobStorage, IDisposable
    {
        private const string ServiceName = "s3";
        private const string Algorithm = "AWS4-HMAC-SHA256";

        private readonly MinioBlobStorageOptions _options;
        private readonly HttpClient _httpClient;
        private readonly bool _disposeHttpClient;
        private readonly ILogger<MinioBlobStorage> _logger;
        private readonly Uri _endpoint;

        public MinioBlobStorage(
            MinioBlobStorageOptions options,
            ILogger<MinioBlobStorage>? logger = null,
            HttpClient? httpClient = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(_options);

            _endpoint = CreateEndpointUri(_options.Endpoint);
            _httpClient = httpClient ?? new HttpClient();
            _disposeHttpClient = httpClient == null;
            _logger = logger ?? NullLogger<MinioBlobStorage>.Instance;
        }

        public Task CheckWriteAsync(CancellationToken cancellationToken = default)
        {
            var payload = Encoding.UTF8.GetBytes(
                $"TemplateProcessor blob storage write check at {DateTimeOffset.UtcNow:O}");
            var stream = new MemoryStream(payload);

            return UploadAsync(
                _options.WriteCheckObjectKey,
                stream,
                "text/plain; charset=utf-8",
                cancellationToken);
        }

        public async Task UploadAsync(
            string objectKey,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            ValidateObjectKey(objectKey);

            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (string.IsNullOrWhiteSpace(contentType))
                throw new ArgumentException("Content type cannot be empty.", nameof(contentType));

            var payload = await ReadPayloadAsync(content, cancellationToken);
            var payloadHash = ToHex(SHA256.HashData(payload));
            var requestUri = BuildObjectUri(objectKey);
            var now = DateTimeOffset.UtcNow;

            using var request = CreateSignedRequest(
                HttpMethod.Put,
                requestUri,
                payload,
                payloadHash,
                contentType,
                now);

            _logger.LogInformation(
                "Uploading object '{ObjectKey}' to MinIO bucket '{BucketName}'. Size: {Size} bytes.",
                objectKey,
                _options.BucketName,
                payload.Length);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogWarning(
                    "MinIO upload failed for object '{ObjectKey}'. Status code: {StatusCode}. Response: {Response}",
                    objectKey,
                    (int)response.StatusCode,
                    error);

                throw new BlobStorageException(
                    $"Failed to upload object '{objectKey}' to bucket '{_options.BucketName}'. " +
                    $"Status code: {(int)response.StatusCode}. Response: {error}");
            }

            _logger.LogInformation(
                "Object '{ObjectKey}' was uploaded to MinIO bucket '{BucketName}' successfully.",
                objectKey,
                _options.BucketName);
        }

        public void Dispose()
        {
            if (_disposeHttpClient)
                _httpClient.Dispose();
        }

        private HttpRequestMessage CreateSignedRequest(
            HttpMethod method,
            Uri requestUri,
            byte[] payload,
            string payloadHash,
            string contentType,
            DateTimeOffset now)
        {
            var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var dateStamp = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var host = GetHostHeader(requestUri);
            var credentialScope = $"{dateStamp}/{_options.Region}/{ServiceName}/aws4_request";

            var canonicalHeaders =
                $"content-type:{contentType}\n" +
                $"host:{host}\n" +
                $"x-amz-content-sha256:{payloadHash}\n" +
                $"x-amz-date:{amzDate}\n";
            var signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
            var canonicalRequest =
                $"{method.Method}\n" +
                $"{requestUri.AbsolutePath}\n" +
                "\n" +
                canonicalHeaders +
                "\n" +
                $"{signedHeaders}\n" +
                payloadHash;
            var stringToSign =
                $"{Algorithm}\n" +
                $"{amzDate}\n" +
                $"{credentialScope}\n" +
                $"{ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";
            var signature = CalculateSignature(_options.SecretKey, dateStamp, _options.Region, stringToSign);

            var request = new HttpRequestMessage(method, requestUri)
            {
                Content = new ByteArrayContent(payload)
            };

            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            request.Headers.Host = host;
            request.Headers.Add("x-amz-content-sha256", payloadHash);
            request.Headers.Add("x-amz-date", amzDate);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                Algorithm,
                $"Credential={_options.AccessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}");

            return request;
        }

        private Uri BuildObjectUri(string objectKey)
        {
            var builder = new UriBuilder(_endpoint);
            var endpointPath = builder.Path.Trim('/');
            var objectPath = $"{EncodePathSegment(_options.BucketName)}/{EncodeObjectKey(objectKey)}";

            builder.Path = string.IsNullOrWhiteSpace(endpointPath)
                ? objectPath
                : $"{endpointPath}/{objectPath}";
            builder.Query = string.Empty;

            return builder.Uri;
        }

        private static string EncodeObjectKey(string objectKey)
        {
            return string.Join(
                "/",
                objectKey
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(EncodePathSegment));
        }

        private static string EncodePathSegment(string value)
        {
            return Uri.EscapeDataString(value);
        }

        private static async Task<byte[]> ReadPayloadAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            if (content.CanSeek)
                content.Position = 0;

            using var memoryStream = new MemoryStream();
            await content.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }

        private static string CalculateSignature(
            string secretKey,
            string dateStamp,
            string region,
            string stringToSign)
        {
            var dateKey = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
            var regionKey = HmacSha256(dateKey, region);
            var serviceKey = HmacSha256(regionKey, ServiceName);
            var signingKey = HmacSha256(serviceKey, "aws4_request");

            return ToHex(HmacSha256(signingKey, stringToSign));
        }

        private static byte[] HmacSha256(byte[] key, string data)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        private static string ToHex(byte[] bytes)
        {
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string GetHostHeader(Uri uri)
        {
            return uri.IsDefaultPort
                ? uri.Host
                : $"{uri.Host}:{uri.Port}";
        }

        private static Uri CreateEndpointUri(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("MinIO endpoint must be an absolute HTTP or HTTPS URL.", nameof(endpoint));
            }

            return uri;
        }

        private static void ValidateOptions(MinioBlobStorageOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Endpoint))
                throw new ArgumentException("MinIO endpoint is required.", nameof(options));

            if (string.IsNullOrWhiteSpace(options.BucketName))
                throw new ArgumentException("MinIO bucket name is required.", nameof(options));

            if (string.IsNullOrWhiteSpace(options.AccessKey))
                throw new ArgumentException("MinIO access key is required.", nameof(options));

            if (string.IsNullOrWhiteSpace(options.SecretKey))
                throw new ArgumentException("MinIO secret key is required.", nameof(options));

            if (string.IsNullOrWhiteSpace(options.Region))
                throw new ArgumentException("MinIO region is required.", nameof(options));

            ValidateObjectKey(options.WriteCheckObjectKey);
        }

        private static void ValidateObjectKey(string objectKey)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
                throw new ArgumentException("Object key cannot be empty.", nameof(objectKey));

            if (objectKey.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("Object key must be relative.", nameof(objectKey));

            if (objectKey.Contains('\\', StringComparison.Ordinal))
                throw new ArgumentException("Object key must use '/' as a separator.", nameof(objectKey));

            var segments = objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".."))
                throw new ArgumentException("Object key cannot contain traversal segments.", nameof(objectKey));
        }
    }
}
