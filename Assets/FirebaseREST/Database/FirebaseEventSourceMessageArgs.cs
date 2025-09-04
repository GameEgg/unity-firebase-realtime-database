using System;
namespace FirebaseREST
{
    public class FirebaseEventSourceMessageArgs : EventArgs
    {
        int id;
        string eventBuffer, dataBuffer;

        public string EventBuffer => eventBuffer;

        public string DataBuffer => dataBuffer;

        public FirebaseEventSourceMessageArgs(string eventBuffer, string dataBuffer)
        {
            this.eventBuffer = eventBuffer;
            this.dataBuffer = dataBuffer;
        }
    }
}