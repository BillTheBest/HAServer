namespace HAServer
{
    public static class Consts
    {

        // USE STATIC READONLY FOR NON ENUMS
        static readonly string TEST = "30";

        // Console error codes (passed to the command line when exiting)
        public enum ExitCodes : byte
        {
            OK = 0,                                             // Normal exit code
            ERR = 1                                             // Fatal error exit code
        }

        // REMOVE IF MICROSOFT.LOGGING IS USED
        // Console error codes (passed to the command line when exiting)
        public enum MessLog : int
        {
            NONE = 0,                                           // Do not write to database log or to the console
            INFO = 1,                                           // Do not write to database log but to console if detailed logs enabled
            MINOR = 2,                                          // Low level function console log
            NORMAL = 3,                                         // Normal function console log
            MAJOR = 4                                           // Display on main console and all related consoles
        }

        // System category codes (stored in the DB as Bytes)
        public enum CatConsts : byte
        {
            ALL = 0,                        // Internal category for all messages (used in tests for triggers)
            SYSTEM = 1                      // Internal category for system messages
        }

    }

}
