using System;

namespace MetaDataIAPlugin
{
    public class AiProviderException : Exception
    {
        public bool StopBatch { get; private set; }
        public string TechnicalDetails { get; private set; }

        public AiProviderException(string message, bool stopBatch = false, string technicalDetails = null) : base(message)
        {
            StopBatch = stopBatch;
            TechnicalDetails = technicalDetails;
        }
    }
}
