namespace TemplateProcessor.Infrastructure.Storage
{
    public class MinioBlobStorageOptions
    {
        public string Endpoint { get; set; } = string.Empty;

        public string BucketName { get; set; } = string.Empty;

        public string AccessKey { get; set; } = string.Empty;

        public string SecretKey { get; set; } = string.Empty;

        public string Region { get; set; } = "us-east-1";

        public string WriteCheckObjectKey { get; set; } = "_checks/template-processor-write-check.txt";
    }
}
