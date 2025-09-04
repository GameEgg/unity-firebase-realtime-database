using System;
namespace FirebaseREST
{
    public class FirebaseEventSourceErrorArgs : EventArgs
    {
        string error;
        public string Error => error;

        public FirebaseEventSourceErrorArgs(string error)
        {
            this.error = error;
        }
    }
}