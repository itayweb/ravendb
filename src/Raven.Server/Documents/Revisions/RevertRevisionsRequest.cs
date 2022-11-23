using System;

namespace Raven.Server.Documents.Revisions
{
    public class RevertRevisionsRequest
    {
        public DateTime Time { get; set; }
        public long WindowInSec { get; set; }
        public string[] Collections { get; set; } = null; 
    }
}
