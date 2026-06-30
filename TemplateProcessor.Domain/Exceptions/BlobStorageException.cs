namespace TemplateProcessor.Domain.Exceptions
{
    public class BlobStorageException : Exception
    {
        public BlobStorageException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
