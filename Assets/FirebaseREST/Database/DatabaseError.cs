namespace FirebaseREST
{
    public class DatabaseError
    {
        FirebaseDatabaseErrorCode code;

        public DatabaseError(FirebaseDatabaseErrorCode code)
        {
            this.code = code;
        }

        public string Message => code.ToString();

        public FirebaseDatabaseErrorCode Code => code;

        public DatabaseException ToException() => new(code.ToString());
    }
}